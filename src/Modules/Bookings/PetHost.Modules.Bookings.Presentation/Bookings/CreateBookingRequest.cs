namespace PetHost.Modules.Bookings.Presentation.Bookings;

/// <summary>Corpo de <c>POST /api/v1/bookings</c>.</summary>
/// <param name="PetIds">Pets do tutor, de 1 a 10, sem repetição.</param>
/// <param name="CheckIn">
/// Dia de chegada, <c>"2026-12-20"</c>. Lido como texto: data mal formada volta como erro do
/// campo, junto com os outros, em vez de um erro genérico do corpo.
/// </param>
/// <param name="CheckOut">Dia de saída, <c>"2026-12-25"</c>.</param>
/// <param name="CareTermsAccepted">
/// <c>true</c>: o tutor confirma que leva a ração e entrega os remédios ao anfitrião (escopo, regra 8).
/// </param>
public sealed record CreateBookingRequest(
    Guid ListingId,
    IReadOnlyList<Guid>? PetIds,
    string? CheckIn,
    string? CheckOut,
    bool? CareTermsAccepted = null);
