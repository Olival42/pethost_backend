using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.SuspendOwner;

/// <summary>
/// Suspende o perfil de tutor e a conta (que derruba as sessões na hora). O perfil vai
/// primeiro; se a conta não puder ser suspensa, ele volta (compensação), para os dois
/// nunca ficarem diferentes.
/// </summary>
public sealed class SuspendOwnerCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnersUnitOfWork unitOfWork,
    IOwnerAccounts ownerAccounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<SuspendOwnerCommand, OwnerResponse>
{
    public async Task<Result<OwnerResponse>> HandleAsync(SuspendOwnerCommand command, CancellationToken cancellationToken)
    {
        var id = new OwnerId(command.OwnerId);
        var owner = await ownerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<OwnerResponse>.Failure(OwnersErrors.NotFound(id));

        var wasSuspended = owner.IsSuspended;
        owner.Suspend(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Result account;
        try
        {
            account = await ownerAccounts
                .SuspendAsync(owner.UserId, command.Reason, command.AdminId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await UndoAsync(owner, wasSuspended).ConfigureAwait(false);
            throw;
        }

        if (account.IsFailure)
        {
            await UndoAsync(owner, wasSuspended).ConfigureAwait(false);
            return Result<OwnerResponse>.FromFailure(account);
        }

        if (!wasSuspended)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.OwnerSuspended, AuditTargets.Owner, owner.Id.Value, command.Reason), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<OwnerResponse>.Success(
            await OwnerResponse.LoadAsync(owner, ownerAccounts, cancellationToken).ConfigureAwait(false));
    }

    private async Task UndoAsync(Owner owner, bool wasSuspended)
    {
        if (wasSuspended)
            return;

        owner.LiftSuspension(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

/// <summary>
/// Tira a suspensão do perfil e da conta. Recusa se o CPF foi liberado. O perfil vai
/// primeiro (é ele que tem a regra); se a conta falhar, ele volta a ficar suspenso.
/// </summary>
public sealed class LiftOwnerSuspensionCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnersUnitOfWork unitOfWork,
    IOwnerAccounts ownerAccounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<LiftOwnerSuspensionCommand, OwnerResponse>
{
    public async Task<Result<OwnerResponse>> HandleAsync(LiftOwnerSuspensionCommand command, CancellationToken cancellationToken)
    {
        var id = new OwnerId(command.OwnerId);
        var owner = await ownerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<OwnerResponse>.Failure(OwnersErrors.NotFound(id));

        var wasSuspended = owner.IsSuspended;
        var lifted = owner.LiftSuspension(timeProvider.GetUtcNow());
        if (lifted.IsFailure)
            return Result<OwnerResponse>.FromFailure(lifted);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Result account;
        try
        {
            account = await ownerAccounts.LiftSuspensionAsync(owner.UserId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await UndoAsync(owner, wasSuspended).ConfigureAwait(false);
            throw;
        }

        if (account.IsFailure)
        {
            await UndoAsync(owner, wasSuspended).ConfigureAwait(false);
            return Result<OwnerResponse>.FromFailure(account);
        }

        if (wasSuspended)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.OwnerSuspensionLifted, AuditTargets.Owner, owner.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<OwnerResponse>.Success(
            await OwnerResponse.LoadAsync(owner, ownerAccounts, cancellationToken).ConfigureAwait(false));
    }

    private async Task UndoAsync(Owner owner, bool wasSuspended)
    {
        if (!wasSuspended)
            return;

        owner.Suspend(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

/// <summary>
/// Libera o CPF de um tutor suspenso, para o dono de verdade poder se cadastrar. A
/// trilha guarda o CPF <b>mascarado</b>, nunca completo.
/// </summary>
public sealed class ReleaseOwnerCpfCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnersUnitOfWork unitOfWork,
    IOwnerAccounts ownerAccounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReleaseOwnerCpfCommand, OwnerResponse>
{
    public async Task<Result<OwnerResponse>> HandleAsync(ReleaseOwnerCpfCommand command, CancellationToken cancellationToken)
    {
        var id = new OwnerId(command.OwnerId);
        var owner = await ownerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<OwnerResponse>.Failure(OwnersErrors.NotFound(id));

        var previous = owner.Cpf;
        var released = owner.ReleaseCpf(timeProvider.GetUtcNow());
        if (released.IsFailure)
            return Result<OwnerResponse>.FromFailure(released);

        if (previous is not null)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await auditTrail
                .RecordAsync(
                    new AuditRecord(
                        AuditActions.OwnerCpfReleased,
                        AuditTargets.Owner,
                        owner.Id.Value,
                        command.Reason,
                        new Dictionary<string, string?> { ["cpf"] = previous.ToString() }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<OwnerResponse>.Success(
            await OwnerResponse.LoadAsync(owner, ownerAccounts, cancellationToken).ConfigureAwait(false));
    }
}
