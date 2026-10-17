using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.SearchListings;

/// <summary>
/// Cantinhos ativos que atendem a todos os filtros enviados (eles se somam). A busca é por raio
/// quando vem <paramref name="RadiusKm"/> ou coordenada, ou quando não vem estado, cidade nem
/// bairro ("perto de casa"); aí a origem é a coordenada ou, sem ela, o endereço de quem busca
/// (o da empresa, para o anfitrião empresa; senão, o da conta).
/// </summary>
/// <param name="UserId">Vem do access token; <c>null</c> para quem busca sem login.</param>
/// <param name="Latitude">Origem opcional (ex.: GPS do celular). Vem junto com <paramref name="Longitude"/>.</param>
/// <param name="RadiusKm">De 5 a 50 km. Na busca por raio, padrão 10.</param>
/// <param name="State">UF, ex.: <c>PR</c>.</param>
/// <param name="Species">Espécies que o cantinho precisa aceitar (todas).</param>
/// <param name="Sizes">Portes que o cantinho precisa aceitar (todos): <c>small</c>, <c>medium</c>, <c>large</c>.</param>
/// <param name="Weekdays">Dias em que o cantinho precisa receber (todos).</param>
/// <param name="HomeType"><c>house</c> ou <c>apartment</c>.</param>
/// <param name="HasDogs">Se na casa mora cachorro (o mesmo para gato e criança).</param>
/// <param name="Page">Começa em 1.</param>
/// <param name="CheckIn">
/// Chegada, <c>yyyy-MM-dd</c>, junto com <paramref name="CheckOut"/>: só cantinhos que recebem em
/// todos os dias do período e têm vaga em todas as noites.
/// </param>
/// <param name="Pets">Quantos pets vão (padrão 1): a vaga precisa caber todos.</param>
/// <param name="Role">
/// Papel do token. Anfitrião empresa (<c>host</c>) busca a partir do endereço da empresa; os outros,
/// do endereço da conta.
/// </param>
public sealed record SearchListingsQuery(
    Guid? UserId,
    decimal? Latitude = null,
    decimal? Longitude = null,
    int? RadiusKm = null,
    string? State = null,
    string? City = null,
    string? Neighborhood = null,
    IReadOnlyList<string>? Species = null,
    IReadOnlyList<string>? Sizes = null,
    IReadOnlyList<string>? Weekdays = null,
    decimal? MinNightlyRate = null,
    decimal? MaxNightlyRate = null,
    int? MinCapacity = null,
    bool? HasYard = null,
    string? HomeType = null,
    bool? HasDogs = null,
    bool? HasCats = null,
    bool? HasKids = null,
    int Page = 1,
    int PageSize = 20,
    string? CheckIn = null,
    string? CheckOut = null,
    int? Pets = null,
    string? Role = null) : IQuery<PagedResult<ListingSummaryResponse>>
{
    /// <summary>Raio da busca por raio quando o cliente não manda: 10 km.</summary>
    public const int DefaultRadiusKm = 10;
}
