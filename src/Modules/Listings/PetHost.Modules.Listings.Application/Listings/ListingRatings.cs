using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Listings;

/// <summary>
/// Qual nota mostrar em cada cantinho: empresa (um cantinho por filial) mostra a nota do cantinho;
/// pessoa física (um cantinho só) mostra a do anfitrião, que continua valendo se ele trocar de
/// cantinho. Uma consulta de cada tipo para a página inteira.
/// </summary>
public static class ListingRatings
{
    /// <summary>A nota de cada cantinho de <paramref name="listings"/>, pelo id do cantinho. Sem avaliação, não aparece.</summary>
    public static async Task<IReadOnlyDictionary<Guid, ListingRating>> ForAsync(
        IListingReviews listingReviews,
        IReadOnlyList<Listing> listings,
        IReadOnlyDictionary<Guid, ListingHostSummary> hosts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listingReviews);
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(hosts);

        if (listings.Count == 0)
            return new Dictionary<Guid, ListingRating>();

        bool IsCompany(Listing listing) => hosts.GetValueOrDefault(listing.HostId) is { IsCompany: true };

        var companyListings = listings.Where(IsCompany).Select(l => l.Id.Value).Distinct().ToList();
        var individualHosts = listings.Where(l => !IsCompany(l)).Select(l => l.HostId).Distinct().ToList();

        var byListing = companyListings.Count == 0
            ? new Dictionary<Guid, ListingRating>()
            : await listingReviews.GetListingRatingsAsync(companyListings, cancellationToken).ConfigureAwait(false);

        var byHost = individualHosts.Count == 0
            ? new Dictionary<Guid, ListingRating>()
            : await listingReviews.GetHostRatingsAsync(individualHosts, cancellationToken).ConfigureAwait(false);

        var ratings = new Dictionary<Guid, ListingRating>();
        foreach (var listing in listings)
        {
            var rating = IsCompany(listing)
                ? byListing.GetValueOrDefault(listing.Id.Value)
                : byHost.GetValueOrDefault(listing.HostId);

            if (rating is not null)
                ratings[listing.Id.Value] = rating;
        }

        return ratings;
    }
}
