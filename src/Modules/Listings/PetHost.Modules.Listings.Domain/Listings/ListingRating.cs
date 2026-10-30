namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// A nota que os tutores deram ao cantinho (ou ao anfitrião, se for pessoa física): média de 1 a 5 e
/// quantas avaliações. Vem do módulo de avaliações; base do selo "Bem avaliado".
/// </summary>
public sealed record ListingRating(decimal Average, int Count);
