using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments.ListMyPayouts;

/// <summary>
/// "Ganhos" do anfitrião: por reserva paga, quanto já foi repassado, quanto está guardado até o
/// check-out e o repasse de cada parcela.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Page">Começa em 1.</param>
public sealed record ListMyPayoutsQuery(Guid UserId, int Page = 1, int PageSize = 20) : IQuery<PagedResult<HostEarningsResponse>>;

/// <summary>Os ganhos de uma reserva.</summary>
/// <param name="PaymentStatus"><c>pending</c>, <c>active</c>, <c>paid</c> ou <c>cancelled</c>.</param>
/// <param name="Payout">Repassado, guardado e estornado.</param>
public sealed record HostEarningsResponse(
    Guid BookingId,
    string PaymentStatus,
    decimal TotalAmount,
    PayoutSummaryResponse Payout,
    IReadOnlyList<HostPayoutItemResponse> Installments);

/// <summary>O repasse de uma parcela.</summary>
/// <param name="Status">Situação da parcela (<c>paid</c>, <c>pending</c>, <c>overdue</c>...).</param>
/// <param name="PayoutStatus"><c>held</c>, <c>transferred</c>, <c>cancelled</c> ou <c>reversed</c>.</param>
/// <param name="Amount">Valor da parcela, sem acréscimo.</param>
/// <param name="HostAmount">Quanto o anfitrião recebe (ou recebeu) da parcela: 90% do que ficou com o PetHost, multa e juros por atraso incluídos.</param>
/// <param name="LateFee">Multa e juros que o tutor pagou a mais na parcela (zero em dia).</param>
public sealed record HostPayoutItemResponse(
    int Number,
    decimal Amount,
    string Status,
    string PayoutStatus,
    decimal HostAmount,
    DateTimeOffset? PaidAt,
    DateTimeOffset? TransferredAt,
    decimal LateFee);

/// <summary>
/// Paginada (§13), do pagamento mais recente para o mais antigo; só reservas com alguma parcela
/// paga. Conta sem perfil de anfitrião: <c>404 PAYMENT_HOST_NOT_FOUND</c>.
/// </summary>
public sealed class ListMyPayoutsQueryHandler(
    IPaymentRepository paymentRepository,
    IPaymentParties parties) : IQueryHandler<ListMyPayoutsQuery, PagedResult<HostEarningsResponse>>
{
    public async Task<Result<PagedResult<HostEarningsResponse>>> HandleAsync(ListMyPayoutsQuery query, CancellationToken cancellationToken)
    {
        var errors = new List<Error>();

        if (query.Page < 1)
            errors.Add(PaymentsErrors.PageInvalid);

        if (query.PageSize is < 1 or > PaymentsErrors.MaxPageSize)
            errors.Add(PaymentsErrors.PageSizeInvalid);

        if (errors.Count > 0)
            return Result<PagedResult<HostEarningsResponse>>.Failure(errors);

        var hostId = await parties.FindHostIdAsync(query.UserId, cancellationToken).ConfigureAwait(false);
        if (hostId is null)
            return Result<PagedResult<HostEarningsResponse>>.Failure(PaymentsErrors.HostNotFound);

        var (items, total) = await paymentRepository
            .SearchPaidByHostAsync(hostId.Value, query.Page, query.PageSize, cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<HostEarningsResponse>>.Success(
            new PagedResult<HostEarningsResponse>([.. items.Select(ToResponse)], query.Page, query.PageSize, total));
    }

    private static HostEarningsResponse ToResponse(Payment payment)
    {
        var installments = payment.Installments.OrderBy(i => i.Number).ToList();

        return new HostEarningsResponse(
            payment.BookingId,
            PaymentStatusValues.ToWire(payment.Status),
            PaymentResponseMapper.Reais(payment.AmountCents),
            PaymentResponseMapper.Payout(installments),
            [.. installments.Select(i => new HostPayoutItemResponse(
                i.Number,
                PaymentResponseMapper.Reais(i.AmountCents),
                InstallmentStatusValues.ToWire(i.Status),
                PayoutStatusValues.ToWire(i.PayoutStatus),
                PaymentResponseMapper.Reais(HostAmount(i)),
                i.PaidAt,
                i.TransferredAt,
                PaymentResponseMapper.Reais(i.IsPaid ? i.LateFeeCents : 0)))]);
    }

    /// <summary>Repassado: o que foi transferido menos o estorno. Ainda não: os 90% previstos do que ficar retido.</summary>
    private static long HostAmount(Installment installment) => installment.PayoutStatus switch
    {
        PayoutStatus.Transferred or PayoutStatus.Reversed => installment.HostPayoutCents - installment.ReversedCents,
        PayoutStatus.Cancelled => 0,
        _ => Payment.HostShare(installment.ChargedCents - installment.RefundedCents),
    };
}
