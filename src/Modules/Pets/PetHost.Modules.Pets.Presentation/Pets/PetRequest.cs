namespace PetHost.Modules.Pets.Presentation.Pets;

/// <summary>
/// Corpo do cadastro (<c>POST /pets</c>) e da alteração (<c>PATCH /pets/{petId}</c>) do pet.
/// O dono vem do token, não daqui. No PATCH, só o que vier muda.
/// </summary>
/// <param name="Species">
/// <c>dog</c>, <c>cat</c>, <c>cockatiel</c>, <c>parrot</c>, <c>parakeet</c>, <c>canary</c>,
/// <c>rabbit</c>, <c>hamster</c>, <c>guinea_pig</c>, <c>fish</c>, <c>turtle</c> ou <c>exotic</c>.
/// </param>
/// <param name="SpeciesDescription">Obrigatória só para <c>exotic</c>: o que é o animal (ex.: "Iguana verde").</param>
/// <param name="Size"><c>small</c>, <c>medium</c> ou <c>large</c>. Obrigatório no cachorro, opcional no gato, proibido nos outros.</param>
/// <param name="BirthDate">Texto <c>yyyy-MM-dd</c>, aproximado. Não pode ser no futuro.</param>
/// <param name="Sex"><c>male</c>, <c>female</c> ou <c>unknown</c>.</param>
/// <param name="WeightKg">Peso aproximado em kg (até 3 casas, ex.: 0.035 = 35 g). No PATCH pode ser trocado, não apagado.</param>
/// <param name="Microchip">15 dígitos; aceita espaços e hífens.</param>
/// <param name="Allergies">Alergias e restrições: alimentos, remédios, produtos.</param>
public sealed record PetRequest(
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
    decimal? WeightKg,
    string? Microchip,
    string? Allergies);
