using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Application.Pets.Responses;

/// <summary>
/// O pet com a ficha inteira e quem é o dono. Nas listas (<see cref="KeeperPetsResponse"/>)
/// o dono vem uma vez no topo, e os três campos do dono saem omitidos de cada pet.
/// </summary>
/// <param name="OwnerId">Id do tutor dono do pet. Omitido quando o pet é de um anfitrião, e nas listas.</param>
/// <param name="HostId">Id do anfitrião dono do pet. Omitido quando o pet é de um tutor, e nas listas.</param>
/// <param name="Keeper">Nome e foto do dono. Omitido nas listas.</param>
/// <param name="Species">Ex.: <c>dog</c>, <c>guinea_pig</c>, <c>exotic</c>.</param>
/// <param name="Size"><c>small</c>, <c>medium</c> ou <c>large</c>. Só cachorro e gato.</param>
/// <param name="Sex"><c>male</c>, <c>female</c> ou <c>unknown</c>.</param>
/// <param name="WeightKg">Peso aproximado em kg, até 3 casas.</param>
/// <param name="Microchip">15 dígitos.</param>
public sealed record PetResponse(
    Guid Id,
    Guid? OwnerId,
    Guid? HostId,
    KeeperSummary? Keeper,
    string Species,
    string? SpeciesDescription,
    string Name,
    string? PhotoUrl,
    string? Breed,
    string? Size,
    DateOnly? BirthDate,
    string Sex,
    bool IsNeutered,
    bool IsVaccinated,
    string? MedicationNotes,
    string? FeedingNotes,
    bool GoodWithDogs,
    bool GoodWithCats,
    bool GoodWithKids,
    string? VetContact,
    string? Notes,
    decimal? WeightKg,
    string? Microchip,
    string? Allergies,
    bool IsActive,
    DateTimeOffset? DeactivatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PetResponse From(Pet pet, KeeperSummary? keeper)
    {
        ArgumentNullException.ThrowIfNull(pet);

        var profile = pet.Profile;

        return new PetResponse(
            pet.Id.Value,
            pet.OwnerId,
            pet.HostId,
            keeper,
            PetSpeciesValues.ToWire(profile.Species),
            profile.SpeciesDescription,
            profile.Name,
            profile.PhotoUrl,
            profile.Breed,
            profile.Size is { } size ? PetSizeValues.ToWire(size) : null,
            profile.BirthDate,
            PetSexValues.ToWire(profile.Sex),
            profile.IsNeutered,
            profile.IsVaccinated,
            profile.MedicationNotes,
            profile.FeedingNotes,
            profile.GoodWithDogs,
            profile.GoodWithCats,
            profile.GoodWithKids,
            profile.VetContact,
            profile.Notes,
            profile.WeightKg,
            profile.Microchip,
            profile.Allergies,
            pet.IsActive,
            pet.DeactivatedAt,
            pet.CreatedAt,
            pet.UpdatedAt);
    }

    /// <summary>O pet como item de lista: sem os campos do dono, que vem no topo da lista.</summary>
    public static PetResponse InList(Pet pet) => From(pet, keeper: null) with { OwnerId = null, HostId = null };

    /// <summary>Um pet, buscando o dono dele.</summary>
    public static async Task<PetResponse> LoadAsync(Pet pet, IPetKeepers petKeepers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pet);
        ArgumentNullException.ThrowIfNull(petKeepers);

        var keepers = await petKeepers.GetSummariesAsync([pet.Keeper], cancellationToken).ConfigureAwait(false);

        return From(pet, keepers.GetValueOrDefault(pet.Keeper));
    }
}
