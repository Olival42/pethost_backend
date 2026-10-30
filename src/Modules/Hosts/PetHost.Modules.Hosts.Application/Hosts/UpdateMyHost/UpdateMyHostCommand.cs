using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Application.Hosts.UpdateMyHost;

/// <summary>
/// Altera o anfitrião logado — só o que vier muda. Livre: telefone, endereço da conta e, na
/// empresa, nome fantasia e endereço da empresa. Travado (identidade conferida na verificação):
/// nome, nascimento, CPF, CNPJ e razão social — mandar o valor atual passa; mudar é recusado.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="CompanyAddress">Endereço da empresa; pode vir pela metade (completa com o atual).</param>
public sealed record UpdateMyHostCommand(
    Guid UserId,
    string? FullName,
    string? Phone,
    AddressData? Address,
    string? BirthDate,
    string? Cpf,
    string? Cnpj,
    string? LegalName,
    string? TradeName,
    AddressData? CompanyAddress) : ICommand<HostResponse>
{
    /// <summary>A parte da conta que pode mudar: telefone e endereço (nome e nascimento são identidade).</summary>
    public AccountProfilePatch ToProfilePatch() => new(FullName: null, Phone: Phone, AvatarUrl: null, Address: Address);
}

/// <summary>Formato do telefone e do endereço, pela regra do Auth. Todos os erros juntos.</summary>
public sealed class UpdateMyHostCommandValidator : AbstractValidator<UpdateMyHostCommand>
{
    public UpdateMyHostCommandValidator(IHostAccounts accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        RuleFor(x => x).CustomErrorsAsync(async (command, context, cancellationToken) =>
        {
            var patch = command.ToProfilePatch();
            if (patch.IsEmpty)
                return;

            var account = await accounts.ValidateProfileAsync(command.UserId, patch, cancellationToken).ConfigureAwait(false);
            if (account.IsSuccess)
                return;

            // Só erros de formato; conta inexistente fica com o handler.
            foreach (var error in account.Errors!.Where(e => e.Code == ErrorCodes.Validation))
                context.AddFailure(error);
        });
    }
}
