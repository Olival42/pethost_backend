using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Listings.Application.Listings;

/// <summary>Registro na trilha das alterações do cantinho: campos pelo nome, nunca pelo valor.</summary>
internal static class ListingTrail
{
    /// <param name="fields">Nomes dos campos (ou <c>photos.added</c>, <c>photos.reordered</c>...), separados por vírgula.</param>
    public static Task RecordUpdatedAsync(IAuditTrail auditTrail, Listing listing, string fields, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ListingUpdated,
                AuditTargets.Listing,
                listing.Id.Value,
                Details: new Dictionary<string, string?> { ["fields"] = fields }),
            cancellationToken);
}
