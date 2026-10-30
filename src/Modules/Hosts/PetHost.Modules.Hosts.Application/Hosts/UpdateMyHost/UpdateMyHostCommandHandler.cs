using System.Globalization;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.UpdateMyHost;

/// <summary>
/// Altera conta (Auth) e perfil (Hosts) num pedido só. Conta e perfil ficam em módulos
/// diferentes: tudo que pode recusar é conferido antes; a conta é gravada primeiro e, se o
/// perfil não puder ser gravado, ela volta ao que era (compensação).
/// </summary>
/// <remarks>
/// <b>Trava de identidade:</b> o anfitrião só existe depois da verificação, então nome,
/// nascimento, CPF, CNPJ e razão social já foram conferidos pelo admin. Mudar qualquer um
/// deles é <c>422 HOST_IDENTITY_LOCKED</c> — exige nova verificação, pelo suporte. Mandar o
/// mesmo valor não é troca: o front pode mandar o formulário inteiro.
/// </remarks>
public sealed class UpdateMyHostCommandHandler(
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UpdateMyHostCommand, HostResponse>
{
    public async Task<Result<HostResponse>> HandleAsync(UpdateMyHostCommand command, CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostResponse>.Failure(HostsErrors.ProfileNotFound);

        var found = await accounts.GetByIdsAsync([command.UserId], cancellationToken).ConfigureAwait(false);
        var account = found.Count > 0 ? found[0] : null;

        if (ChangesIdentity(command, host, account))
            return Result<HostResponse>.Failure(HostsErrors.IdentityLocked);

        // Dados da empresa que podem mudar: conferidos (e completados com o atual) antes de gravar.
        var companyChanges = command.TradeName is not null || command.CompanyAddress is not null;
        CompanyAddress? newCompanyAddress = null;
        if (companyChanges)
        {
            if (host.PersonType != PersonType.Company)
                return Result<HostResponse>.Failure(HostsErrors.NotACompany);

            if (command.CompanyAddress is { } patch)
            {
                var current = host.CompanyAddress!;
                var address = CompanyAddress.Create(
                    patch.ZipCode ?? current.ZipCode,
                    patch.Street ?? current.Street,
                    patch.Number ?? current.Number,
                    patch.Complement is null ? current.Complement : patch.Complement,
                    patch.Neighborhood ?? current.Neighborhood,
                    patch.City ?? current.City,
                    patch.State ?? current.State);
                if (address.IsFailure)
                    return Result<HostResponse>.FromFailure(address);

                newCompanyAddress = address.Value;
            }
        }

        AccountProfilePatch? previous = null;
        var profile = command.ToProfilePatch();
        if (!profile.IsEmpty)
        {
            var updated = await accounts.UpdateProfileAsync(command.UserId, profile, cancellationToken).ConfigureAwait(false);
            if (updated.IsFailure)
                return Result<HostResponse>.FromFailure(updated);

            previous = updated.Value;
        }

        if (companyChanges)
        {
            Result saved;
            try
            {
                saved = host.ChangeCompanyContact(command.TradeName, newCompanyAddress, timeProvider.GetUtcNow());
                if (saved.IsSuccess)
                    saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await UndoAsync(command.UserId, previous).ConfigureAwait(false);
                throw;
            }

            if (saved.IsFailure)
            {
                await UndoAsync(command.UserId, previous).ConfigureAwait(false);
                return Result<HostResponse>.FromFailure(saved);
            }

            // Os dados da conta já ficam na trilha pelo Auth (account.profile_updated).
            var fields = new List<string>();
            if (command.TradeName is not null)
                fields.Add("tradeName");
            if (command.CompanyAddress is not null)
                fields.Add("companyAddress");

            await auditTrail
                .RecordAsync(
                    new AuditRecord(AuditActions.HostUpdated, AuditTargets.Host, host.Id.Value, Details: new Dictionary<string, string?> { ["fields"] = string.Join(",", fields) }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostResponse>.Success(await HostResponse.LoadAsync(host, accounts, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Algum dado de identidade veio diferente do atual? Valor inválido conta como diferente.</summary>
    private static bool ChangesIdentity(UpdateMyHostCommand command, Host host, AccountSummary? account)
    {
        if (command.FullName is { } name && !string.Equals(name.Trim(), account?.FullName, StringComparison.Ordinal))
            return true;

        if (command.BirthDate is { } birthDate
            && (!DateOnly.TryParseExact(birthDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date != account?.BirthDate))
            return true;

        if (command.Cpf is not null && Cpf.Create(command.Cpf) is var cpf && (cpf.IsFailure || cpf.Value != host.Cpf))
            return true;

        if (command.Cnpj is not null && Cnpj.Create(command.Cnpj) is var cnpj && (cnpj.IsFailure || cnpj.Value != host.Cnpj))
            return true;

        return command.LegalName is { } legalName && !string.Equals(legalName.Trim(), host.LegalName, StringComparison.Ordinal);
    }

    /// <summary>Compensação: devolve a conta ao perfil anterior, mesmo se o pedido caiu.</summary>
    private async Task UndoAsync(Guid userId, AccountProfilePatch? previous)
    {
        if (previous is not null)
            await accounts.UpdateProfileAsync(userId, previous, CancellationToken.None).ConfigureAwait(false);
    }
}
