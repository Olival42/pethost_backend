using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Application.Pets.UpdatePet;

/// <summary>
/// Altera a ficha de um pet da conta logada (PATCH): só muda o que vier. <c>null</c> mantém
/// o valor atual; texto vazio (<c>""</c>) limpa um campo opcional.
/// </summary>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="PetId">Vem da rota.</param>
public sealed record UpdatePetCommand(
    Guid UserId,
    string Role,
    Guid PetId,
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
    string? Allergies = null) : ICommand<PetResponse>;
