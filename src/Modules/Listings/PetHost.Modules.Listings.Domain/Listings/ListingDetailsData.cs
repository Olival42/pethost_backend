namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// O que o anfitrião descreve no cantinho, como chega (texto, <c>bool?</c>, listas — ainda sem
/// validar). Entrada de <see cref="ListingDetails.Create"/>, no cadastro e no PATCH já mesclado.
/// </summary>
/// <param name="HomeType"><c>house</c> ou <c>apartment</c>. Opcional para empresa.</param>
/// <param name="AcceptedSpecies">Tipos aceitos, mesmos valores de <c>pets.species</c> (<c>dog</c>, <c>guinea_pig</c>...).</param>
/// <param name="Capacity">Quantos pets ao mesmo tempo, somando todos os tutores.</param>
/// <param name="NightlyRate">Diária por pet, em reais, até 2 casas (60.00 = R$ 60,00).</param>
/// <param name="AvailableWeekdays">Dias em que recebe pets: <c>monday</c> ... <c>sunday</c>.</param>
public sealed record ListingDetailsData(
    string? Title,
    string? Description,
    string? HomeType,
    bool? HasYard,
    bool? HasDogs,
    bool? HasCats,
    bool? HasKids,
    IReadOnlyList<string?>? AcceptedSpecies,
    bool? AcceptsSmall,
    bool? AcceptsMedium,
    bool? AcceptsLarge,
    int? Capacity,
    decimal? NightlyRate,
    string? HouseRules,
    IReadOnlyList<string?>? AvailableWeekdays);
