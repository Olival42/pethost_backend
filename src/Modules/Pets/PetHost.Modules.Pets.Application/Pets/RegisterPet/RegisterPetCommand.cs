using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Application.Pets.RegisterPet;

/// <summary>
/// Cadastra um pet da conta logada: do tutor (pet que vai se hospedar) ou do anfitrião
/// (pet que mora na casa dele).
/// </summary>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>. Decide se o pet é do tutor ou do anfitrião.</param>
public sealed record RegisterPetCommand(
    Guid UserId,
    string Role,
    string? Species,
    string? SpeciesDescription,
    string? Name,
    string? PhotoUrl,
    string? Breed,
    string? Size,
    string? BirthDate,
    string? Sex,
    bool? IsNeutered,
    bool? IsVaccinated,
    string? MedicationNotes,
    string? FeedingNotes,
    bool? GoodWithDogs,
    bool? GoodWithCats,
    bool? GoodWithKids,
    string? VetContact,
    string? Notes,
    decimal? WeightKg = null,
    string? Microchip = null,
    string? Allergies = null) : ICommand<PetResponse>
{
    public PetProfileData ToProfileData() =>
        new(
            Species,
            SpeciesDescription,
            Name,
            PhotoUrl,
            Breed,
            Size,
            BirthDate,
            Sex,
            IsNeutered,
            IsVaccinated,
            MedicationNotes,
            FeedingNotes,
            GoodWithDogs,
            GoodWithCats,
            GoodWithKids,
            VetContact,
            Notes,
            WeightKg,
            Microchip,
            Allergies);
}
