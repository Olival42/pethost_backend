namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>
/// Os ganhos do anfitrião num mês (horário de Brasília), em centavos: o painel do anfitrião. O mês
/// conta pela data do repasse (<see cref="Installment.TransferredAt"/>) e do estorno
/// (<see cref="Installment.ReversedAt"/>), não pela da reserva: é o dinheiro que chegou (ou saiu)
/// nele. O guardado e o previsto são sempre os de agora.
/// </summary>
/// <param name="Transferred">Repassado no mês, antes de estornos.</param>
/// <param name="Reversed">Estornado no mês.</param>
/// <param name="PreviousMonthTransferred">Repassado no mês anterior.</param>
/// <param name="YearToDateTransferred">Repassado de 1º de janeiro até o fim do mês.</param>
/// <param name="PaidByOwners">O que os tutores pagaram nas parcelas repassadas no mês (multa incluída).</param>
/// <param name="LateFees">Multa e juros por atraso dessas parcelas.</param>
/// <param name="Refunded">Devolvido ao tutor nessas parcelas.</param>
/// <param name="PlatformFee">Os 10% do PetHost nessas parcelas.</param>
/// <param name="AwaitingCheckOut">Guardado: estadia ainda sem check-out.</param>
/// <param name="Disputed">Guardado: contestação aberta.</param>
/// <param name="Blocked">Guardado: o repasse não passou (Stripe bloqueou ou recusou) ou o reembolso falhou.</param>
/// <param name="Expected">Os 90% previstos das parcelas ainda por pagar nos pagamentos em andamento.</param>
/// <param name="ExpectedInstallments">Quantas parcelas ainda por pagar.</param>
public sealed record HostEarningsReport(
    long Transferred,
    long Reversed,
    long PreviousMonthTransferred,
    long YearToDateTransferred,
    long PaidByOwners,
    long LateFees,
    long Refunded,
    long PlatformFee,
    long AwaitingCheckOut,
    long Disputed,
    long Blocked,
    long Expected,
    int ExpectedInstallments)
{
    /// <summary>Os ganhos do mês de <paramref name="month"/> (qualquer dia dele) nos pagamentos do anfitrião.</summary>
    public static HostEarningsReport For(IReadOnlyCollection<Payment> payments, DateOnly month)
    {
        ArgumentNullException.ThrowIfNull(payments);

        var firstDay = new DateOnly(month.Year, month.Month, 1);
        var monthStart = PaymentClock.StartOf(firstDay);
        var monthEnd = PaymentClock.StartOf(firstDay.AddMonths(1));
        var previousStart = PaymentClock.StartOf(firstDay.AddMonths(-1));
        var yearStart = PaymentClock.StartOf(new DateOnly(month.Year, 1, 1));

        var installments = payments.SelectMany(p => p.Installments).ToList();

        // Estornada também foi repassada: conta no mês do repasse; o estorno, no mês dele.
        var transferred = installments
            .Where(i => i.PayoutStatus is PayoutStatus.Transferred or PayoutStatus.Reversed)
            .ToList();
        var thisMonth = transferred.Where(i => Within(i.TransferredAt, monthStart, monthEnd)).ToList();

        var held = installments.Where(i => i.IsPaid && i.PayoutStatus == PayoutStatus.Held).ToList();
        var disputed = held.Where(i => i.DisputeStatus == DisputeStatus.Open).ToList();
        var blocked = held
            .Where(i => i.DisputeStatus != DisputeStatus.Open
                && (i.PayoutFailureCode is not null || i.RefundStatus == RefundStatus.Failed))
            .ToList();

        var toPay = payments
            .Where(p => p.Status == PaymentStatus.Active)
            .SelectMany(p => p.Installments)
            .Where(i => i.Status is InstallmentStatus.Pending or InstallmentStatus.Processing or InstallmentStatus.Overdue)
            .ToList();

        return new HostEarningsReport(
            thisMonth.Sum(i => i.HostPayoutCents),
            installments.Where(i => Within(i.ReversedAt, monthStart, monthEnd)).Sum(i => i.ReversedCents),
            transferred.Where(i => Within(i.TransferredAt, previousStart, monthStart)).Sum(i => i.HostPayoutCents),
            transferred.Where(i => Within(i.TransferredAt, yearStart, monthEnd)).Sum(i => i.HostPayoutCents),
            thisMonth.Sum(i => i.ChargedCents),
            thisMonth.Sum(i => i.LateFeeCents),
            thisMonth.Sum(i => i.RefundedCents),
            thisMonth.Sum(i => i.PlatformFeeCents),
            held.Except(disputed).Except(blocked).Sum(HeldShare),
            disputed.Sum(HeldShare),
            blocked.Sum(HeldShare),
            toPay.Sum(i => Payment.HostShare(i.AmountCents)),
            toPay.Count);
    }

    /// <summary>Os 90% previstos do que ficou retido na parcela (mesma conta de "Ganhos").</summary>
    private static long HeldShare(Installment installment) =>
        Payment.HostShare(installment.ChargedCents - installment.RefundedCents);

    private static bool Within(DateTimeOffset? at, DateTimeOffset from, DateTimeOffset to) => at >= from && at < to;
}
