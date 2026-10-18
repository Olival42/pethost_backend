using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Listings.Responses;

/// <summary>A nota dos tutores no cartão e no detalhe: média com 1 casa ("4,8") e quantas avaliações.</summary>
public sealed record ListingRatingResponse(decimal Average, int Count)
{
    /// <summary>Nulo quando ninguém avaliou (o campo some do JSON).</summary>
    public static ListingRatingResponse? From(ListingRating? rating) =>
        rating is null
            ? null
            : new ListingRatingResponse(Math.Round(rating.Average, 1, MidpointRounding.AwayFromZero), rating.Count);
}
