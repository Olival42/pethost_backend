using PetHost.Modules.Bookings.Application.Abstractions;

namespace PetHost.Modules.Bookings.Application.Bookings.Responses;

/// <summary>
/// A reserva, como o tutor e o anfitrião a veem. Datas de calendário em ISO 8601
/// (<c>"2026-12-20"</c>); valores em reais.
/// </summary>
/// <param name="Status">
/// <c>requested</c>, <c>accepted</c>, <c>confirmed</c>, <c>in_progress</c>, <c>completed</c>,
/// <c>declined</c>, <c>expired</c> ou <c>cancelled</c>.
/// </param>
/// <param name="Listing">Título e bairro do cantinho (nulo se ele sumiu).</param>
/// <param name="Owner">Nome e foto do tutor.</param>
/// <param name="Host">Nome e foto do anfitrião (empresa pelo nome fantasia).</param>
/// <param name="Nights">Saída − chegada.</param>
/// <param name="NightlyRate">Diária por pet copiada no pedido.</param>
/// <param name="TotalPrice">Diária × noites × pets: o que o tutor paga.</param>
/// <param name="ResponseDeadline">Até quando o anfitrião pode responder. Só enquanto <c>requested</c>.</param>
/// <param name="PaymentDeadline">
/// Até quando o tutor pode pagar a 1ª parcela (24h depois do aceite, sem passar do dia de
/// chegada). Só enquanto <c>accepted</c>; sem pagar, a reserva expira.
/// </param>
/// <param name="CancelledBy"><c>owner</c>, <c>host</c> ou <c>platform</c> (parcela não paga antes do check-in).</param>
/// <param name="RefundPercentage">Quanto volta para o tutor no cancelamento de reserva paga (100 ou 50).</param>
/// <param name="Contact">
/// O contato do outro lado, liberado só depois da confirmação (escopo, regra 7): para o tutor, o
/// telefone do anfitrião e o endereço exato do cantinho; para o anfitrião, o telefone do tutor.
/// Omitido antes disso, depois de cancelada e para o admin.
/// </param>
/// <param name="OwnerRating">
/// A nota do tutor (o que os anfitriões escreveram dele: média com 1 casa e contagem), para o
/// anfitrião decidir sobre o pedido. Só para o anfitrião e o admin; omitida enquanto ninguém avaliou.
/// </param>
/// <param name="CareTermsAcceptedAt">
/// Quando o tutor confirmou que leva a ração e entrega os remédios (escopo, regra 8). Omitido nas
/// reservas pedidas antes dessa confirmação existir.
/// </param>
public sealed record BookingResponse(
    Guid Id,
    string Status,
    Guid ListingId,
    BookingListingSummary? Listing,
    Guid OwnerId,
    PartySummary? Owner,
    Guid HostId,
    PartySummary? Host,
    IReadOnlyList<BookingPetResponse> Pets,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int PetCount,
    decimal NightlyRate,
    decimal TotalPrice,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResponseDeadline,
    DateTimeOffset? PaymentDeadline,
    DateTimeOffset? RespondedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CheckedInAt,
    DateTimeOffset? CheckedOutAt,
    DateTimeOffset? ExpiredAt,
    DateTimeOffset? CancelledAt,
    string? CancelledBy,
    string? CancellationReason,
    string? DeclineReason,
    int? RefundPercentage,
    BookingContactResponse? Contact,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    BookingOwnerRatingResponse? OwnerRating = null,
    DateTimeOffset? CareTermsAcceptedAt = null);

/// <summary>Contato liberado depois da confirmação.</summary>
/// <param name="Phone">Telefone do outro lado (nulo se a conta não tem).</param>
/// <param name="Address">Endereço exato do cantinho — só para o tutor.</param>
public sealed record BookingContactResponse(string? Phone, BookingAddress? Address);

/// <summary>A nota do tutor: média com 1 casa ("4,8") e quantas avaliações.</summary>
public sealed record BookingOwnerRatingResponse(decimal Average, int Count);

/// <summary>Um pet da reserva: nome, espécie e porte (nulos se a ficha sumiu).</summary>
public sealed record BookingPetResponse(Guid Id, string? Name, string? Species, string? Size);
