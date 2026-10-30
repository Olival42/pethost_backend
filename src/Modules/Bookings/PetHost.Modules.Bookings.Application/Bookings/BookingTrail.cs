using System.Globalization;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Bookings.Application.Bookings;

/// <summary>
/// Registro na trilha das mudanças de status da reserva. Motivo em texto livre de tutor e
/// anfitrião não entra; o do admin entra (§15: toda ação do admin sobre dado de outra pessoa).
/// </summary>
internal static class BookingTrail
{
    public static Task RecordAsync(
        IAuditTrail auditTrail,
        string action,
        Booking booking,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? details = null,
        string? adminReason = null) =>
        auditTrail.RecordAsync(new AuditRecord(action, AuditTargets.Booking, booking.Id.Value, adminReason, details), cancellationToken);

    /// <summary>Cancelamento: quem cancelou e o reembolso (quando a reserva estava paga).</summary>
    /// <param name="adminReason">Motivo do admin, quando foi ele quem cancelou.</param>
    /// <param name="cause">Por que a plataforma cancelou sozinha (<c>owner_suspended</c>, <c>host_suspended</c>).</param>
    public static Task RecordCancelledAsync(
        IAuditTrail auditTrail,
        Booking booking,
        CancellationToken cancellationToken,
        string? adminReason = null,
        string? cause = null)
    {
        var details = new Dictionary<string, string?>
        {
            ["by"] = booking.CancelledBy is { } by ? BookingPartyValues.ToWire(by) : null,
            ["refundPercentage"] = booking.RefundPercentage?.ToString(CultureInfo.InvariantCulture),
        };

        if (cause is not null)
            details["cause"] = cause;

        return RecordAsync(auditTrail, AuditActions.BookingCancelled, booking, cancellationToken, details, adminReason);
    }
}
