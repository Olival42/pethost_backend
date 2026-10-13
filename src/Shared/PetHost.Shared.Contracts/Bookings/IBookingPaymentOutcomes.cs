namespace PetHost.Shared.Contracts.Bookings;

/// <summary>
/// Contrato do módulo Bookings para o que o pagamento decide sobre a reserva (§5). O Payments
/// chama isto; o Bookings aplica a regra no agregado, grava, registra na trilha e avisa por e-mail.
/// </summary>
/// <remarks>
/// Quem chama é o próprio Payments, que em seguida reconcilia o pagamento: por isso estas
/// mudanças não avisam o Payments de volta.
/// </remarks>
public interface IBookingPaymentOutcomes
{
    /// <summary>
    /// A 1ª parcela foi paga: a reserva aceita vira confirmada (ocupa vaga e libera o contato).
    /// Já confirmada (ou além): <see cref="BookingPaymentOutcome.AlreadyDone"/>. Cancelada ou
    /// expirada enquanto o tutor pagava: <see cref="BookingPaymentOutcome.NotApplicable"/> — o
    /// pagamento devolve o dinheiro.
    /// </summary>
    Task<BookingPaymentOutcome> ConfirmPaidAsync(Guid bookingId, CancellationToken cancellationToken);

    /// <summary>
    /// Uma parcela venceu antes do check-in e passou da tolerância: a reserva confirmada é
    /// cancelada pela plataforma com 100% de reembolso. Já cancelada:
    /// <see cref="BookingPaymentOutcome.AlreadyDone"/>; check-in feito ou outro status:
    /// <see cref="BookingPaymentOutcome.NotApplicable"/>.
    /// </summary>
    Task<BookingPaymentOutcome> CancelUnpaidAsync(Guid bookingId, CancellationToken cancellationToken);
}
