using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments.ListPaymentIssues;

/// <summary>
/// Pendências de pagamento para o admin (lista administrativa, sem paginação — §13): parcela em
/// atraso, contestação aberta ou perdida, reembolso que falhou, repasse ou estorno que não passou.
/// </summary>
public sealed record ListPaymentIssuesQuery : IQuery<IReadOnlyList<PaymentIssueResponse>>;

/// <summary>Uma pendência numa parcela.</summary>
/// <param name="Issue">
/// <c>installment_overdue</c>, <c>dispute_open</c>, <c>dispute_lost</c>, <c>refund_failed</c>,
/// <c>payout_failed</c> ou <c>payout_reversal_failed</c>.
/// </param>
/// <param name="Since">Desde quando (atraso, contestação, reembolso) — quando se sabe.</param>
/// <param name="FailureCode">Por que o repasse ou o estorno falhou (código do Stripe ou <c>NO_CONNECTED_ACCOUNT</c>).</param>
/// <param name="StripeObjectId">O objeto a conferir no Dashboard do Stripe.</param>
public sealed record PaymentIssueResponse(
    Guid PaymentId,
    Guid BookingId,
    int InstallmentNumber,
    string Issue,
    decimal Amount,
    DateTimeOffset? Since,
    string? FailureCode,
    string? StripeObjectId);

/// <summary>Da pendência mais antiga para a mais nova.</summary>
public sealed class ListPaymentIssuesQueryHandler(IPaymentRepository paymentRepository)
    : IQueryHandler<ListPaymentIssuesQuery, IReadOnlyList<PaymentIssueResponse>>
{
    public async Task<Result<IReadOnlyList<PaymentIssueResponse>>> HandleAsync(ListPaymentIssuesQuery query, CancellationToken cancellationToken)
    {
        var payments = await paymentRepository.ListNeedingAttentionAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PaymentIssueResponse> issues =
        [
            .. payments
                .SelectMany(payment => payment.Installments.SelectMany(installment => IssuesOf(payment, installment)))
                .OrderBy(issue => issue.Since ?? DateTimeOffset.MaxValue),
        ];

        return Result<IReadOnlyList<PaymentIssueResponse>>.Success(issues);
    }

    private static IEnumerable<PaymentIssueResponse> IssuesOf(Payment payment, Installment installment)
    {
        PaymentIssueResponse Issue(string issue, long cents, DateTimeOffset? since, string? stripeObjectId, string? failureCode = null) =>
            new(payment.Id.Value, payment.BookingId, installment.Number, issue, PaymentResponseMapper.Reais(cents), since, failureCode, stripeObjectId);

        if (installment.Status == InstallmentStatus.Overdue)
            yield return Issue("installment_overdue", installment.AmountCents, installment.OverdueSince, installment.StripePaymentIntentId);

        if (installment.DisputeStatus == DisputeStatus.Open)
            yield return Issue("dispute_open", installment.AmountCents, installment.DisputedAt, installment.StripeDisputeId);

        // Perdida: o repasse já feito precisa ser estornado; se ainda não foi, é pendência.
        if (installment.DisputeStatus == DisputeStatus.Lost)
        {
            yield return installment.PayoutStatus == PayoutStatus.Transferred
                ? Issue("payout_reversal_failed", installment.HostPayoutCents, installment.DisputeClosedAt, installment.StripeTransferId, installment.PayoutFailureCode)
                : Issue("dispute_lost", installment.AmountCents, installment.DisputeClosedAt, installment.StripeDisputeId);
        }

        if (installment.RefundStatus == RefundStatus.Failed)
            yield return Issue("refund_failed", installment.RefundedCents, installment.RefundedAt, installment.StripeRefundId);

        if (installment.PayoutStatus == PayoutStatus.Held && installment.PayoutFailureCode is { } code)
            yield return Issue("payout_failed", Payment.HostShare(installment.ChargedCents - installment.RefundedCents), installment.PaidAt, installment.StripeChargeId, code);
    }
}
