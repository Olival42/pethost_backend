namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Identidade de <see cref="AvailabilityBlock"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct AvailabilityBlockId(Guid Value)
{
    public static AvailabilityBlockId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
