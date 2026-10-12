namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// Busca de cantinhos <b>ativos</b>: filtros somados (E) e, opcionalmente, um raio. Com
/// <paramref name="Area"/>, vem do mais perto para o mais longe; sem, do mais novo para o mais antigo.
/// </summary>
/// <param name="Area">Raio em volta de um ponto. Nulo: sem filtro de distância.</param>
/// <param name="ExcludedHostIds">Anfitriões que não aparecem (inativos ou suspensos).</param>
/// <param name="Page">Começa em 1.</param>
public sealed record ListingSearch(
    SearchArea? Area,
    ListingFilters Filters,
    IReadOnlyCollection<Guid> ExcludedHostIds,
    int Page,
    int PageSize);

/// <summary>Os cantinhos a até <paramref name="RadiusKm"/> de <paramref name="Origin"/>.</summary>
public sealed record SearchArea(GeoPoint Origin, int RadiusKm);

/// <summary>
/// Filtros da busca; o que vier nulo (ou vazio) não filtra. Todos se somam: o cantinho precisa
/// atender a cada um.
/// </summary>
/// <param name="State">UF em maiúsculas, como fica gravada.</param>
/// <param name="City">Nome exato, sem diferença de acento nem de maiúsculas/minúsculas.</param>
/// <param name="Neighborhood">Nome exato, sem diferença de acento nem de maiúsculas/minúsculas.</param>
/// <param name="Species">O cantinho precisa aceitar <b>todas</b> estas espécies.</param>
/// <param name="AcceptsSmall">Verdadeiro: precisa aceitar porte pequeno.</param>
/// <param name="Weekdays">O cantinho precisa receber em <b>todos</b> estes dias.</param>
/// <param name="MinCapacity">Recebe pelo menos esta quantidade de pets.</param>
/// <param name="HomeType">Tipo da casa. Cantinho de empresa (sem tipo) não entra quando este filtro vem.</param>
/// <param name="HasDogs">Se na casa mora cachorro.</param>
public sealed record ListingFilters(
    string? State = null,
    string? City = null,
    string? Neighborhood = null,
    AcceptedSpecies Species = AcceptedSpecies.None,
    bool AcceptsSmall = false,
    bool AcceptsMedium = false,
    bool AcceptsLarge = false,
    Weekdays Weekdays = Weekdays.None,
    decimal? MinNightlyRate = null,
    decimal? MaxNightlyRate = null,
    int? MinCapacity = null,
    bool? HasYard = null,
    HomeType? HomeType = null,
    bool? HasDogs = null,
    bool? HasCats = null,
    bool? HasKids = null);

/// <summary>Um cantinho achado na busca. <paramref name="DistanceKm"/> só vem na busca por raio.</summary>
public sealed record FoundListing(Listing Listing, double? DistanceKm);
