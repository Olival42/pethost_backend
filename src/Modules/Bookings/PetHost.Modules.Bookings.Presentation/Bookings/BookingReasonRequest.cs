namespace PetHost.Modules.Bookings.Presentation.Bookings;

/// <summary>Corpo opcional da recusa e do cancelamento. Pode ser omitido.</summary>
/// <param name="Reason">Opcional, até 500 caracteres.</param>
public sealed record BookingReasonRequest(string? Reason);
