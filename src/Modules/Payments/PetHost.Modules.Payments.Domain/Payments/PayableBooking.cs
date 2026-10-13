namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>A reserva aceita que vai ser paga: quem paga, quem recebe, quanto e até quando pagar a 1ª parcela.</summary>
/// <param name="AmountCents">Valor total da estadia, em centavos.</param>
/// <param name="PaymentDeadline">Prazo da 1ª parcela (24h depois do aceite, sem passar do dia de chegada).</param>
public sealed record PayableBooking(Guid BookingId, Guid OwnerId, Guid HostId, long AmountCents, DateTimeOffset PaymentDeadline)
{
    /// <summary>Reais (como a reserva guarda) para centavos (como o Stripe cobra), arredondando o meio para cima.</summary>
    public static long ToCents(decimal reais) => (long)Math.Round(reais * 100m, MidpointRounding.AwayFromZero);
}
