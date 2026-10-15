using System.Globalization;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Listings.Application.Listings.AvailabilityBlocks;

/// <summary>Um período fechado na agenda do cantinho.</summary>
/// <param name="StartDate">Primeiro dia bloqueado (<c>yyyy-MM-dd</c>).</param>
/// <param name="EndDate">Último dia bloqueado, inclusive.</param>
/// <param name="Days">Quantos dias o bloqueio fecha.</param>
/// <param name="Reason">Motivo — só para o anfitrião dono e o admin; para os outros, omitido.</param>
public sealed record AvailabilityBlockResponse(
    Guid Id,
    Guid ListingId,
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    string? Reason,
    DateTimeOffset CreatedAt)
{
    public static AvailabilityBlockResponse From(AvailabilityBlock block, bool withReason)
    {
        ArgumentNullException.ThrowIfNull(block);

        return new(block.Id.Value, block.ListingId.Value, block.StartDate, block.EndDate, block.Days, withReason ? block.Reason : null, block.CreatedAt);
    }
}

/// <summary>Leitura das datas da agenda, no formato das outras datas da API (<c>yyyy-MM-dd</c>).</summary>
internal static class AvailabilityDates
{
    public static bool TryParse(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), StayWindow.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>Registro na trilha dos bloqueios: o período, nunca o motivo (texto livre).</summary>
internal static class AvailabilityTrail
{
    public static Task RecordAsync(IAuditTrail auditTrail, string action, AvailabilityBlock block, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                action,
                AuditTargets.Listing,
                block.ListingId.Value,
                Details: new Dictionary<string, string?>
                {
                    ["blockId"] = block.Id.Value.ToString(),
                    ["startDate"] = block.StartDate.ToString(StayWindow.DateFormat, CultureInfo.InvariantCulture),
                    ["endDate"] = block.EndDate.ToString(StayWindow.DateFormat, CultureInfo.InvariantCulture),
                }),
            cancellationToken);
}
