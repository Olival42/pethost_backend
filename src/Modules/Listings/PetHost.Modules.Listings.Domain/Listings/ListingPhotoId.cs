namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Identidade de <see cref="ListingPhoto"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct ListingPhotoId(Guid Value)
{
    public static ListingPhotoId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
