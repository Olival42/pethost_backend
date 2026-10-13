using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Listings.Responses;

/// <summary>
/// Um cantinho na lista da busca: o cartão com a capa, o bairro e a distância ("Jardim Alvorada ·
/// 2 km de você"). Sem endereço nem coordenada. O detalhe sai em <c>GET /listings/{id}</c>.
/// </summary>
/// <param name="DistanceKm">Distância em linha reta até a origem da busca, com 1 casa. Omitida quando a busca não é por raio (só estado/cidade/bairro).</param>
/// <param name="CoverPhotoUrl">A primeira foto. Omitida se o cantinho não tem foto.</param>
/// <param name="Badges">Selos automáticos: <c>neighbor</c>, <c>fast_responder</c>, <c>yard</c>, <c>experienced</c>.</param>
public sealed record ListingSummaryResponse(
    Guid Id,
    Guid HostId,
    ListingHostSummary? Host,
    string Title,
    string Neighborhood,
    string City,
    string State,
    double? DistanceKm,
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
    IReadOnlyList<string> Badges)
{
    public static ListingSummaryResponse From(FoundListing found, ListingHostSummary? host, IReadOnlyList<string> badges)
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
            badges);
    }
}
