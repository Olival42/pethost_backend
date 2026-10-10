using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.ReactivatePet;

/// <summary>
/// Reativa o pet. Só o dono; idempotente — reativar um pet ativo não grava nada. Se, enquanto
/// estava desativado, outro pet ativo ficou com o mesmo microchip, devolve
/// <c>409 PET_MICROCHIP_ALREADY_REGISTERED</c>.
/// </summary>
public sealed class ReactivatePetCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IPetKeepers petKeepers,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReactivatePetCommand, PetResponse>
{
    public async Task<Result<PetResponse>> HandleAsync(ReactivatePetCommand command, CancellationToken cancellationToken)
    {
        var owned = await PetAccess
            .LoadOwnedAsync(petRepository, petKeepers, command.UserId, command.Role, command.PetId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<PetResponse>.FromFailure(owned);

        var pet = owned.Value!;

        if (!pet.IsActive)
        {
            if (pet.Profile.Microchip is { } microchip
                && await petRepository.MicrochipInUseAsync(microchip, pet.Id, cancellationToken).ConfigureAwait(false))
            {
                return Result<PetResponse>.Failure(PetsErrors.MicrochipAlreadyRegistered);
            }

            pet.Reactivate(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.PetReactivated, AuditTargets.Pet, pet.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<PetResponse>.Success(
            await PetResponse.LoadAsync(pet, petKeepers, cancellationToken).ConfigureAwait(false));
    }
}
