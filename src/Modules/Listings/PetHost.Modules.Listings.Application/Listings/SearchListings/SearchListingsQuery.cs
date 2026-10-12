using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.SearchListings;

/// <summary>
/// Cantinhos ativos ao redor de um ponto, do mais perto para o mais longe. Sem coordenada, a
/// origem é o endereço da conta logada ("perto de casa").
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Latitude">Origem opcional (ex.: GPS do celular). Vem junto com <paramref name="Longitude"/>.</param>
/// <param name="RadiusKm">De 5 a 50 km. Padrão 10.</param>
/// <param name="Page">Começa em 1.</param>
public sealed record SearchListingsQuery(
    Guid UserId,
    decimal? Latitude = null,
    decimal? Longitude = null,
    int RadiusKm = SearchListingsQuery.DefaultRadiusKm,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ListingSummaryResponse>>
{
    /// <summary>Raio quando o cliente não manda: 10 km.</summary>
    public const int DefaultRadiusKm = 10;
}
