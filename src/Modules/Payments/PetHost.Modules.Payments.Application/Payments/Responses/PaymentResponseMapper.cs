using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Payments;

namespace PetHost.Modules.Payments.Application.Payments.Responses;

/// <summary>Monta <see cref="PaymentResponse"/>: o que aparece depende de quem vê (<see cref="PaymentViewer"/>).</summary>
public static class PaymentResponseMapper
{
    /// <summary>Valor de <see cref="PaymentResponse.Status"/> antes de o tutor escolher as parcelas.</summary>
    public const string NotStarted = "not_started";

    public static PaymentResponse ToResponse(Payment? payment, PaymentBooking booking, PaymentViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var awaiting = booking.Stage == BookingStage.AwaitingPayment;
        var canChoose = awaiting && viewer != PaymentViewer.Host && (payment?.CanChangePlan ?? true);

        IReadOnlyList<InstallmentOptionResponse>? options = canChoose
            ? [.. Payment.OptionsFor(payment?.AmountCents ?? booking.AmountCents)
                .Select(o => new InstallmentOptionResponse(o.Count, Reais(o.FirstAmountCents), Reais(o.OtherAmountCents)))]
            : null;

        if (payment is null)
        {
            return new PaymentResponse(
                null,
                booking.Id,
                NotStarted,
                Reais(booking.AmountCents),
                0m,
                0m,
                Payment.Currency,
                null,
                awaiting ? booking.PaymentDeadline : null,
                null,
                [],
                options,
                null,
                null,
                null);
        }

        var installments = payment.Installments.OrderBy(i => i.Number).ToList();

        return new PaymentResponse(
            payment.Id.Value,
            payment.BookingId,
            PaymentStatusValues.ToWire(payment.Status),
            Reais(payment.AmountCents),
            Reais(payment.PaidCents),
            Reais(installments.Where(i => i.RefundStatus is RefundStatus.Pending or RefundStatus.Succeeded).Sum(i => i.RefundedCents)),
            Payment.Currency,
            payment.InstallmentCount,
            awaiting ? booking.PaymentDeadline : null,
            payment.RefundPercentage,
            [.. installments.Select(i => ToResponse(i, viewer))],
            options,
            viewer == PaymentViewer.Owner ? null : Payout(installments),
            payment.CreatedAt,
            payment.UpdatedAt);
    }

    private static InstallmentResponse ToResponse(Installment installment, PaymentViewer viewer) => new(
        installment.Number,
        Reais(installment.AmountCents),
        installment.DueAt,
        InstallmentStatusValues.ToWire(installment.Status),
        installment.Method is { } method ? PaymentMethodValues.ToWire(method) : null,
        installment.PaidAt,
        viewer == PaymentViewer.Host ? null : installment.FailureCode,
        RefundStatusValues.ToWire(installment.RefundStatus),
        installment.RefundStatus == RefundStatus.None ? null : Reais(installment.RefundedCents),
        installment.RefundedAt,
        PayoutStatusValues.ToWire(installment.PayoutStatus),
        installment.PayoutStatus is PayoutStatus.Transferred or PayoutStatus.Reversed ? Reais(installment.HostPayoutCents - installment.ReversedCents) : null,
        installment.TransferredAt,
        DisputeStatusValues.ToWire(installment.DisputeStatus),
        viewer == PaymentViewer.Admin
            ? new InstallmentStripeIds(
                installment.StripePaymentIntentId,
                installment.StripeChargeId,
                installment.StripeRefundId,
                installment.StripeTransferId,
                installment.StripeTransferReversalId,
                installment.StripeDisputeId,
                installment.PayoutFailureCode)
            : null);

    /// <summary>Transferido (menos estornos), guardado (90% do que foi pago e ainda não repassado) e estornado.</summary>
    public static PayoutSummaryResponse Payout(IReadOnlyCollection<Installment> installments)
    {
        ArgumentNullException.ThrowIfNull(installments);

        var transferred = installments
            .Where(i => i.PayoutStatus is PayoutStatus.Transferred or PayoutStatus.Reversed)
            .Sum(i => i.HostPayoutCents - i.ReversedCents);

        var held = installments
            .Where(i => i.IsPaid && i.PayoutStatus == PayoutStatus.Held)
            .Sum(i => Payment.HostShare(i.AmountCents - i.RefundedCents));

        var reversed = installments.Sum(i => i.ReversedCents);

        return new PayoutSummaryResponse(Reais(transferred), Reais(held), Reais(reversed));
    }

    /// <summary>Centavos (como o Stripe) para reais (como a reserva).</summary>
    public static decimal Reais(long cents) => cents / 100m;
}
