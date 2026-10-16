namespace PetHost.Shared.Contracts.Bookings;

/// <summary>
/// A reserva vista pelo módulo de pagamento (<see cref="IBookingDirectory.GetForPaymentAsync"/>).
/// Só o que o pagamento usa: quem paga, quem recebe, quanto, o status e as datas.
/// </summary>
/// <param name="OwnerId">Tutor que paga (<c>owner.owners.id</c>).</param>
/// <param name="HostId">Anfitrião que recebe o repasse (<c>host.hosts.id</c>).</param>
/// <param name="Status">Status da reserva, nos valores <c>snake_case</c> da API (<see cref="Accepted"/>...).</param>
/// <param name="TotalPrice">Valor total da estadia, em reais.</param>
/// <param name="PaymentDeadline">
/// Até quando a 1ª parcela pode ser paga (24h depois do aceite, sem passar do dia de chegada). Só
/// enquanto a reserva está <see cref="Accepted"/>.
/// </param>
/// <param name="RefundPercentage">Quanto volta para o tutor, quando a reserva paga foi cancelada.</param>
public sealed record BookingPaymentView(
    Guid Id,
    Guid OwnerId,
    Guid HostId,
    string Status,
    decimal TotalPrice,
    DateOnly CheckIn,
    DateOnly CheckOut,
    DateTimeOffset? PaymentDeadline,
    int? RefundPercentage)
{
    public const string Requested = "requested";
    public const string Accepted = "accepted";
    public const string Confirmed = "confirmed";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Declined = "declined";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
}
