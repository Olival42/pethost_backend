using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.UpdatePet;

/// <summary>
/// Altera a ficha de um pet da conta logada. Só o dono mexe; a ficha mesclada é validada
/// inteira pelo domínio. Ficha igual à atual não grava nada. Microchip novo que já é de
/// outro pet ativo devolve <c>409 PET_MICROCHIP_ALREADY_REGISTERED</c>.
/// </summary>
public sealed class UpdatePetCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IPetKeepers petKeepers,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UpdatePetCommand, PetResponse>
{
    public async Task<Result<PetResponse>> HandleAsync(UpdatePetCommand command, CancellationToken cancellationToken)
    {
        var owned = await PetAccess
            .LoadOwnedAsync(petRepository, petKeepers, command.UserId, command.Role, command.PetId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<PetResponse>.FromFailure(owned);

        var pet = owned.Value!;

        var now = timeProvider.GetUtcNow();

        var before = pet.Profile.ToData();
        var profile = PetProfile.Create(PetPatch.Apply(before, command), now);
        if (profile.IsFailure)
            return Result<PetResponse>.FromFailure(profile);

        // Pet inativo não disputa o número; a checagem fica para quando for reativado.
        if (pet.IsActive
            && profile.Value!.Microchip is { } microchip
            && microchip != before.Microchip
            && await petRepository.MicrochipInUseAsync(microchip, pet.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result<PetResponse>.Failure(PetsErrors.MicrochipAlreadyRegistered);
        }

        if (pet.UpdateProfile(profile.Value!, now))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var changed = PetPatch.ChangedFields(before, profile.Value!.ToData());

            await auditTrail
                .RecordAsync(
                    new AuditRecord(
                        AuditActions.PetUpdated,
                        AuditTargets.Pet,
                        pet.Id.Value,
                        Details: new Dictionary<string, string?> { ["fields"] = string.Join(",", changed) }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<PetResponse>.Success(
            await PetResponse.LoadAsync(pet, petKeepers, cancellationToken).ConfigureAwait(false));
    }
}
