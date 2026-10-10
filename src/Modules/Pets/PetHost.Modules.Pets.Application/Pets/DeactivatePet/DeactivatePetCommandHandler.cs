using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.DeactivatePet;

/// <summary>Desativa o pet. Só o dono; idempotente — desativar de novo não grava nada.</summary>
public sealed class DeactivatePetCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IPetKeepers petKeepers,
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
