namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Identidade de <see cref="Listing"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct ListingId(Guid Value)
{
    public static ListingId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
