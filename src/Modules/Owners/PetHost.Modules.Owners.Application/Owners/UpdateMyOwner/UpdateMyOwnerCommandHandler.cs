using System.Globalization;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.UpdateMyOwner;

/// <summary>
/// Altera conta e CPF do tutor logado. CPF e data de nascimento identificam o pagador
/// (Stripe, verificação de documentos): trocar qualquer um pede a senha atual e só vale
/// até o primeiro pagamento. CPF de outro tutor é recusado. Valor igual ao atual não é
/// troca: não pede senha nem esbarra na trava.
/// </summary>
/// <remarks>
/// Conta e perfil ficam em módulos diferentes, sem uma transação única. Tudo que pode
/// recusar o pedido é conferido antes de gravar; a conta é gravada primeiro e, se gravar
/// o CPF falhar, o perfil anterior da conta é aplicado de volta (compensação).
/// </remarks>
public sealed class UpdateMyOwnerCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnersUnitOfWork unitOfWork,
    IOwnerAccounts ownerAccounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UpdateMyOwnerCommand, OwnerResponse>
{
    public async Task<Result<OwnerResponse>> HandleAsync(
        UpdateMyOwnerCommand command,
        CancellationToken cancellationToken)
    {
        Cpf? newCpf = null;
        if (command.Cpf is not null)
        {
            var cpf = Cpf.Create(command.Cpf);
            if (cpf.IsFailure)
                return Result<OwnerResponse>.FromFailure(cpf);

            newCpf = cpf.Value;
        }

        var owner = await ownerRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<OwnerResponse>.Failure(OwnersErrors.ProfileNotFound);

        // Mesmo CPF ou mesma data de antes não é troca: não pede senha nem confere nada.
        var cpfChanges = newCpf is not null && newCpf != owner.Cpf;
        var birthDateChanges = command.BirthDate is not null
            && await BirthDateChangesAsync(command.UserId, command.BirthDate, cancellationToken).ConfigureAwait(false);

        if (cpfChanges || birthDateChanges)
        {
            var allowed = await CheckSensitiveChangeAsync(owner, cpfChanges ? newCpf : null, birthDateChanges, command, cancellationToken)
                .ConfigureAwait(false);
            if (allowed.IsFailure)
                return Result<OwnerResponse>.FromFailure(allowed);
        }

        AccountProfilePatch? previous = null;
        var patch = command.ToProfilePatch();
        if (!patch.IsEmpty)
        {
            var updated = await ownerAccounts
                .UpdateProfileAsync(command.UserId, patch, cancellationToken)
                .ConfigureAwait(false);

            if (updated.IsFailure)
                return Result<OwnerResponse>.FromFailure(updated);

            previous = updated.Value;
        }

        if (cpfChanges)
        {
            try
            {
                var changed = owner.ChangeCpf(newCpf!, timeProvider.GetUtcNow());
                if (changed.IsFailure)
                {
                    await UndoAsync(command.UserId, previous).ConfigureAwait(false);
                    return Result<OwnerResponse>.FromFailure(changed);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await UndoAsync(command.UserId, previous).ConfigureAwait(false);
                throw;
            }
        }

        // Os dados da conta já ficam na trilha pelo Auth (account.profile_updated); aqui,
        // só o que é sensível do tutor. CPF mascarado, nunca completo.
        if (cpfChanges || birthDateChanges)
        {
            var details = new Dictionary<string, string?>();
            if (cpfChanges)
                details["cpf"] = newCpf!.ToString();
            if (birthDateChanges)
                details["birthDateChanged"] = "true";

            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.OwnerUpdated, AuditTargets.Owner, owner.Id.Value, Details: details), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<OwnerResponse>.Success(
            await OwnerResponse.LoadAsync(owner, ownerAccounts, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// A data enviada é diferente da atual? Data em formato inválido conta como troca —
    /// o validador já a recusou antes de chegar aqui.
    /// </summary>
    private async Task<bool> BirthDateChangesAsync(Guid userId, string birthDate, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(birthDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return true;

        var accounts = await ownerAccounts.GetByIdsAsync([userId], cancellationToken).ConfigureAwait(false);

        return accounts.Count == 0 || accounts[0].BirthDate != date;
    }

    /// <summary>
    /// Ordem das recusas: travado primeiro (depois do pagamento a resposta é sempre "não
    /// pode"), depois a senha, e só então a unicidade do CPF — assim quem não sabe a
    /// senha não descobre se um CPF está cadastrado.
    /// </summary>
    private async Task<Result> CheckSensitiveChangeAsync(
        Owner owner,
        Cpf? newCpf,
        bool birthDateChanges,
        UpdateMyOwnerCommand command,
        CancellationToken cancellationToken)
    {
        if (newCpf is not null && !owner.CanChangeCpf)
            return Result.Failure(OwnersErrors.CpfLocked);

        if (birthDateChanges && !owner.CanChangeBirthDate)
            return Result.Failure(OwnersErrors.BirthDateLocked);

        if (string.IsNullOrEmpty(command.CurrentPassword))
            return Result.Failure(OwnersErrors.CurrentPasswordRequired);

        var passwordMatches = await ownerAccounts
            .VerifyPasswordAsync(command.UserId, command.CurrentPassword, cancellationToken)
            .ConfigureAwait(false);

        if (!passwordMatches)
            return Result.Failure(OwnersErrors.CurrentPasswordIncorrect);

        if (newCpf is not null && await ownerRepository.ExistsByCpfAsync(newCpf, cancellationToken).ConfigureAwait(false))
            return Result.Failure(OwnersErrors.CpfAlreadyRegistered);

        return Result.Success();
    }

    /// <summary>
    /// Compensação: devolve a conta ao perfil anterior. <c>CancellationToken.None</c>:
    /// roda mesmo se o request caiu.
    /// </summary>
    private async Task UndoAsync(Guid userId, AccountProfilePatch? previous)
    {
        if (previous is not null)
            await ownerAccounts.UpdateProfileAsync(userId, previous, CancellationToken.None).ConfigureAwait(false);
    }
}
