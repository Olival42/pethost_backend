namespace PetHost.Modules.Listings.Presentation.Listings;

/// <summary>
/// Corpo do cadastro (<c>POST /listings</c>) e da alteração (<c>PATCH /listings/{listingId}</c>)
/// do cantinho. O anfitrião vem do token, não daqui; a coordenada sai do endereço. No PATCH, só o
/// que vier muda. As fotos não vêm aqui: têm rotas próprias (<c>/listings/{listingId}/photos</c>).
/// </summary>
/// <param name="HomeType"><c>house</c> ou <c>apartment</c>. Obrigatório para pessoa física; opcional para empresa.</param>
/// <param name="AcceptedSpecies">
/// Ao menos um: <c>dog</c>, <c>cat</c>, <c>cockatiel</c>, <c>parrot</c>, <c>parakeet</c>, <c>canary</c>,
/// <c>rabbit</c>, <c>hamster</c>, <c>guinea_pig</c>, <c>fish</c>, <c>turtle</c>, <c>exotic</c>.
/// </param>
/// <param name="AcceptsSmall">Porte pequeno. Com cachorro ou gato aceito, ao menos um porte é obrigatório.</param>
/// <param name="Capacity">Pets ao mesmo tempo: até 10 (pessoa física) ou 200 (empresa, por filial).</param>
/// <param name="NightlyRate">Diária por pet, em reais, até 2 casas: de 10.00 a 10000.00.</param>
/// <param name="AvailableWeekdays">Ao menos um: <c>monday</c>, <c>tuesday</c>, <c>wednesday</c>, <c>thursday</c>, <c>friday</c>, <c>saturday</c>, <c>sunday</c>.</param>
/// <param name="Address">Endereço do cantinho (cada filial tem o seu). No PATCH também é parcial.</param>
public sealed record ListingRequest(
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
    IReadOnlyList<string?>? AvailableWeekdays,
    ListingAddressRequest? Address);

/// <summary>Endereço do cantinho: o mesmo formato do endereço da conta.</summary>
/// <param name="ZipCode">CEP, com ou sem máscara.</param>
/// <param name="Number">Aceita "S/N".</param>
/// <param name="State">UF, ex.: <c>PR</c>.</param>
public sealed record ListingAddressRequest(
    string? ZipCode,
    string? Street,
    string? Number,
    string? Complement,
    string? Neighborhood,
    string? City,
    string? State);

/// <summary>Corpo de <c>PUT /listings/{listingId}/photos/order</c>.</summary>
/// <param name="PhotoIds">Todas as fotos do cantinho, cada uma uma vez, na ordem nova. A primeira é a capa.</param>
public sealed record ReorderPhotosRequest(IReadOnlyList<Guid>? PhotoIds);

/// <summary>Corpo de <c>POST /listings/{listingId}/availability-blocks</c>.</summary>
/// <param name="StartDate">Primeiro dia bloqueado, <c>yyyy-MM-dd</c>.</param>
/// <param name="EndDate">Último dia bloqueado, inclusive (pode ser o mesmo do início).</param>
/// <param name="Reason">Opcional, até 120 caracteres, só para o anfitrião.</param>
public sealed record AvailabilityBlockRequest(string? StartDate, string? EndDate, string? Reason);
