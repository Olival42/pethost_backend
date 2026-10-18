using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Listings.Responses;

/// <summary>
/// Um cantinho na lista da busca: o cartão com a capa, o bairro e a distância ("Jardim Alvorada ·
/// 2 km de você") e o ponto aproximado para o mapa. Sem endereço nem coordenada exata. O detalhe
/// sai em <c>GET /listings/{id}</c>.
/// </summary>
/// <param name="DistanceKm">Distância em linha reta da origem da busca até o ponto aproximado, com 1 casa (erro de até 0,5 km). Omitida quando a busca não é por raio (só estado/cidade/bairro).</param>
/// <param name="ApproximateLocation">Ponto aproximado e o raio do círculo (500 m) para o mapa.</param>
/// <param name="CoverPhotoUrl">A primeira foto. Omitida se o cantinho não tem foto.</param>
/// <param name="Badges">Selos automáticos: <c>neighbor</c>, <c>fast_responder</c>, <c>yard</c>, <c>experienced</c>, <c>well_rated</c>.</param>
/// <param name="Rating">Nota dos tutores (média com 1 casa e contagem). Omitida enquanto ninguém avaliou.</param>
public sealed record ListingSummaryResponse(
    Guid Id,
    Guid HostId,
    ListingHostSummary? Host,
    string Title,
    string Neighborhood,
    string City,
    string State,
    double? DistanceKm,
    ListingApproximateLocationResponse ApproximateLocation,
    decimal NightlyRate,
    int Capacity,
    string? HomeType,
    bool HasYard,
    IReadOnlyList<string> AcceptedSpecies,
    bool AcceptsSmall,
    bool AcceptsMedium,
    bool AcceptsLarge,
    IReadOnlyList<string> AvailableWeekdays,
    string? CoverPhotoUrl,
    IReadOnlyList<string> Badges,
    ListingRatingResponse? Rating = null)
{
    public static ListingSummaryResponse From(
        FoundListing found,
        ListingHostSummary? host,
        IReadOnlyList<string> badges,
        ListingRating? rating = null)
    {
        ArgumentNullException.ThrowIfNull(found);

        var listing = found.Listing;
        var details = listing.Details;

        return new ListingSummaryResponse(
            listing.Id.Value,
            listing.HostId,
            host,
            details.Title,
            listing.Address.Neighborhood,
            listing.Address.City,
            listing.Address.State,
            found.DistanceKm is { } distance ? Math.Round(distance, 1, MidpointRounding.AwayFromZero) : null,
            ListingApproximateLocationResponse.From(listing),
            details.NightlyRate,
            details.Capacity,
            details.HomeType is { } homeType ? HomeTypeValues.ToWire(homeType) : null,
            details.HasYard,
            AcceptedSpeciesValues.ToWire(details.AcceptedSpecies),
            details.AcceptsSmall,
            details.AcceptsMedium,
            details.AcceptsLarge,
            WeekdaysValues.ToWire(details.AvailableWeekdays),
            listing.Photos is [var cover, ..] ? cover.Url : null,
            badges,
            ListingRatingResponse.From(rating));
    }
}
