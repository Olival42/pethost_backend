using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.RegisterPet;

/// <summary>
/// Liga o pet ao tutor ou ao anfitrião da conta logada e grava a ficha. A conta sem esse
/// perfil ainda não tem a quem ligar o pet (<c>404 PET_KEEPER_NOT_FOUND</c>). Microchip de
/// outro pet ativo é <c>409 PET_MICROCHIP_ALREADY_REGISTERED</c>.
/// </summary>
public sealed class RegisterPetCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IPetKeepers petKeepers,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<RegisterPetCommand, PetResponse>
{
    public async Task<Result<PetResponse>> HandleAsync(RegisterPetCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // O validador já barrou ficha inválida; o domínio é a garantia de que nenhum
        // caminho grava pet fora da regra.
        var profile = PetProfile.Create(command.ToProfileData(), now);
        if (profile.IsFailure)
            return Result<PetResponse>.FromFailure(profile);

        var keeper = await petKeepers
            .FindByAccountAsync(command.UserId, command.Role, cancellationToken)
            .ConfigureAwait(false);

        if (keeper is null)
            return Result<PetResponse>.Failure(PetsErrors.KeeperNotFound);

        if (profile.Value!.Microchip is { } microchip
            && await petRepository.MicrochipInUseAsync(microchip, exceptPetId: null, cancellationToken).ConfigureAwait(false))
        {
            return Result<PetResponse>.Failure(PetsErrors.MicrochipAlreadyRegistered);
        }

        var pet = Pet.Create(keeper.Value, profile.Value!, now);

        petRepository.Add(pet);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.PetRegistered,
                    AuditTargets.Pet,
                    pet.Id.Value,
                    Details: new Dictionary<string, string?>
                    {
                        ["keeper"] = keeper.Value.TypeName,
                        ["species"] = PetSpeciesValues.ToWire(pet.Profile.Species),
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PetResponse>.Success(
            await PetResponse.LoadAsync(pet, petKeepers, cancellationToken).ConfigureAwait(false));
    }
}
