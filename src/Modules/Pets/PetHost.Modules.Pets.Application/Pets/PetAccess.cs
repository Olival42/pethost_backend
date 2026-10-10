using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets;

/// <summary>Quem pode mexer em qual pet: só o dono dele (tutor ou anfitrião).</summary>
public static class PetAccess
{
    /// <summary>
    /// O pet <paramref name="petId"/>, rastreado, se for da conta logada. Pet inexistente e pet
    /// de outra conta respondem igual, <c>404 PET_NOT_FOUND</c>: quem não é dono não descobre
    /// que o id existe (um 403 confirmaria).
    /// </summary>
    public static async Task<Result<Pet>> LoadOwnedAsync(
        IPetRepository petRepository,
        IPetKeepers petKeepers,
        Guid userId,
        string role,
        Guid petId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(petRepository);
        ArgumentNullException.ThrowIfNull(petKeepers);

        var id = new PetId(petId);
        var pet = await petRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (pet is null)
            return Result<Pet>.Failure(PetsErrors.NotFound(id));

        var keeper = await petKeepers.FindByAccountAsync(userId, role, cancellationToken).ConfigureAwait(false);
        if (keeper is null || !pet.IsKeptBy(keeper.Value))
            return Result<Pet>.Failure(PetsErrors.NotFound(id));

        return Result<Pet>.Success(pet);
    }
}
