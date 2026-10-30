using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.DeactivatePet;

/// <summary>
/// Desativa o pet. Só o dono; idempotente — desativar de novo não grava nada. Pet de tutor numa
/// reserva em aberto (solicitada, aceita, confirmada ou em andamento) não é desativado:
/// <c>422 PET_HAS_OPEN_BOOKING</c> — a estadia precisa ser cancelada antes.
/// </summary>
public sealed class DeactivatePetCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IPetKeepers petKeepers,
    IPetBookings petBookings,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<DeactivatePetCommand, PetResponse>
{
    public async Task<Result<PetResponse>> HandleAsync(DeactivatePetCommand command, CancellationToken cancellationToken)
    {
        var owned = await PetAccess
            .LoadOwnedAsync(petRepository, petKeepers, command.UserId, command.Role, command.PetId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<PetResponse>.FromFailure(owned);

        var pet = owned.Value!;

        if (pet.IsActive)
        {
            // Pet de anfitrião não se hospeda: só o do tutor pode estar numa reserva.
            if (pet.Keeper.Type == KeeperType.Owner
                && await petBookings.HasOpenBookingAsync(pet.Id.Value, cancellationToken).ConfigureAwait(false))
            {
                return Result<PetResponse>.Failure(PetsErrors.HasOpenBooking);
            }

            pet.Deactivate(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.PetDeactivated, AuditTargets.Pet, pet.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<PetResponse>.Success(
            await PetResponse.LoadAsync(pet, petKeepers, cancellationToken).ConfigureAwait(false));
    }
}
