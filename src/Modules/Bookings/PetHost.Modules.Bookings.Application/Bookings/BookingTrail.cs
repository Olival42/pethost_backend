using System.Globalization;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Bookings.Application.Bookings;

/// <summary>Registro na trilha das mudanças de status da reserva. Motivo em texto livre não entra.</summary>
internal static class BookingTrail
{
    public static Task RecordAsync(
        IAuditTrail auditTrail,
        string action,
        Booking booking,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? details = null) =>
        auditTrail.RecordAsync(new AuditRecord(action, AuditTargets.Booking, booking.Id.Value, Details: details), cancellationToken);

    /// <summary>Cancelamento: quem cancelou e o reembolso (quando a reserva estava paga).</summary>
    public static Task RecordCancelledAsync(IAuditTrail auditTrail, Booking booking, CancellationToken cancellationToken) =>
        RecordAsync(
            auditTrail,
            AuditActions.BookingCancelled,
            booking,
            cancellationToken,
            new Dictionary<string, string?>
            {
                ["by"] = booking.CancelledBy is { } by ? BookingPartyValues.ToWire(by) : null,
                ["refundPercentage"] = booking.RefundPercentage?.ToString(CultureInfo.InvariantCulture),
            });
}
