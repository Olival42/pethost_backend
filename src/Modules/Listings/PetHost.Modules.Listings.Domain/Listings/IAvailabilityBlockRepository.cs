namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Acesso a <see cref="AvailabilityBlock"/>. A Application só conhece esta interface (§11).</summary>
public interface IAvailabilityBlockRepository
{
    /// <summary>Pelo id, só se for do cantinho <paramref name="listingId"/>. Rastreado.</summary>
    Task<AvailabilityBlock?> GetAsync(ListingId listingId, AvailabilityBlockId id, CancellationToken cancellationToken);

    /// <summary>
    /// Os bloqueios do cantinho que ainda não acabaram (terminam em <paramref name="today"/> ou
    /// depois), do mais próximo para o mais distante.
    /// </summary>
    Task<IReadOnlyList<AvailabilityBlock>> ListOpenAsync(ListingId listingId, DateOnly today, CancellationToken cancellationToken);

    void Add(AvailabilityBlock block);

    void Remove(AvailabilityBlock block);
}
