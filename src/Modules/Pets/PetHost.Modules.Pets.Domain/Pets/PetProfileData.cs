namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>
/// A ficha do pet como chega (texto e <c>bool?</c>, ainda sem validar). É a entrada de
/// <see cref="PetProfile.Create"/>, no cadastro e no PATCH já mesclado com a ficha atual.
/// </summary>
/// <param name="Species">Valor de <see cref="PetSpeciesValues"/>, ex.: <c>dog</c>, <c>guinea_pig</c>.</param>
/// <param name="SpeciesDescription">O que é o animal, quando <c>exotic</c>. Ex.: "Iguana verde".</param>
/// <param name="Size"><c>small</c>, <c>medium</c> ou <c>large</c>. Obrigatório no cachorro, opcional no gato.</param>
/// <param name="BirthDate">Texto <c>yyyy-MM-dd</c>. Nascimento aproximado.</param>
/// <param name="Sex"><c>male</c>, <c>female</c> ou <c>unknown</c>.</param>
/// <param name="WeightKg">Peso aproximado em kg (até 3 casas: 0,035 = 35 g).</param>
/// <param name="Microchip">Número do microchip: 15 dígitos (ISO 11784/11785).</param>
/// <param name="Allergies">Alergias e restrições: alimentos, remédios, produtos.</param>
public sealed record PetProfileData(
    string? Species,
    string? SpeciesDescription,
    string? Name,
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
    string? Allergies = null);
