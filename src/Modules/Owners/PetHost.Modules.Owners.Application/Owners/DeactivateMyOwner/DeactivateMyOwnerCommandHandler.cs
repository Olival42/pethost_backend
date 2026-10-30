using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.DeactivateMyOwner;

/// <summary>
/// Inativa o perfil de tutor e a conta ligada, o que derruba todas as sessões na hora. A
/// outra conta da mesma pessoa (anfitrião) não é afetada. Com reserva em aberto, recusa
/// (<c>422 OWNER_HAS_OPEN_BOOKING</c>).
/// </summary>
/// <remarks>
/// O perfil é gravado primeiro; se inativar a conta falhar, o perfil volta a ficar ativo
/// (compensação), para os dois nunca ficarem com status diferentes.
/// </remarks>
public sealed class DeactivateMyOwnerCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnersUnitOfWork unitOfWork,
    IOwnerAccounts ownerAccounts,
    IOwnerBookings ownerBookings,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<DeactivateMyOwnerCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        DeactivateMyOwnerCommand command,
        CancellationToken cancellationToken)
    {
        var owner = await ownerRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<Unit>.Failure(OwnersErrors.ProfileNotFound);

        var wasActive = owner.IsActive;

        // Com reserva em aberto, inativar deixaria a estadia sem ninguém do lado do tutor:
        // a conta inativa não entra nem para cancelar.
        if (wasActive && await ownerBookings.HasOpenBookingAsync(owner.Id.Value, cancellationToken).ConfigureAwait(false))
            return Result<Unit>.Failure(OwnersErrors.HasOpenBooking);

        owner.Deactivate(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Result account;
        try
        {
            account = await ownerAccounts.DeactivateAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await UndoAsync(owner, wasActive).ConfigureAwait(false);
            throw;
        }

        if (account.IsFailure)
        {
            await UndoAsync(owner, wasActive).ConfigureAwait(false);
            return Result<Unit>.FromFailure(account);
        }

        if (wasActive)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.OwnerDeactivated, AuditTargets.Owner, owner.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>Compensação: volta o perfil a ativo. Roda mesmo se o request caiu.</summary>
    private async Task UndoAsync(Owner owner, bool wasActive)
    {
        if (!wasActive)
            return;

        owner.Reactivate(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
