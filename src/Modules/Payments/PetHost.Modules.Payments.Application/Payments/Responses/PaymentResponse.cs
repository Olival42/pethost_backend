namespace PetHost.Modules.Payments.Application.Payments.Responses;

/// <summary>
/// O pagamento da reserva, como o tutor, o anfitrião e o admin o veem. Valores em reais (como a
/// reserva), datas em ISO 8601 UTC. Nulos são omitidos.
/// </summary>
/// <param name="Id">Nulo enquanto o tutor não escolheu as parcelas (<c>status = not_started</c>).</param>
/// <param name="Status"><c>not_started</c>, <c>pending</c>, <c>active</c>, <c>paid</c> ou <c>cancelled</c>.</param>
/// <param name="TotalAmount">Valor da reserva.</param>
/// <param name="PaidAmount">Quanto já foi pago.</param>
/// <param name="RefundedAmount">Quanto já foi (ou está sendo) devolvido.</param>
/// <param name="PaymentDeadline">Até quando pagar a 1ª parcela, enquanto a reserva espera o pagamento.</param>
/// <param name="RefundPercentage">Quanto do que foi pago volta ao tutor (reserva cancelada).</param>
/// <param name="Options">
/// As formas de parcelar (1x a 6x, cada parcela de pelo menos R$ 50,00) — para o tutor e o admin,
/// enquanto o número de parcelas ainda pode ser escolhido ou trocado.
/// </param>
/// <param name="Payout">O repasse ao anfitrião — só para o anfitrião e o admin.</param>
public sealed record PaymentResponse(
    Guid? Id,
    Guid BookingId,
    string Status,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal RefundedAmount,
    string Currency,
    int? InstallmentCount,
    DateTimeOffset? PaymentDeadline,
    int? RefundPercentage,
    IReadOnlyList<InstallmentResponse> Installments,
    IReadOnlyList<InstallmentOptionResponse>? Options,
    PayoutSummaryResponse? Payout,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Uma parcela.</summary>
/// <param name="Status"><c>pending</c>, <c>processing</c>, <c>paid</c>, <c>overdue</c> ou <c>cancelled</c>.</param>
/// <param name="Method"><c>card</c> ou <c>pix</c>, quando o tutor pagou.</param>
/// <param name="FailureCode">Por que a última tentativa falhou (código do Stripe). Não aparece para o anfitrião.</param>
/// <param name="RefundStatus"><c>none</c>, <c>pending</c>, <c>succeeded</c> ou <c>failed</c>.</param>
/// <param name="PayoutStatus"><c>held</c>, <c>transferred</c>, <c>cancelled</c> ou <c>reversed</c>.</param>
/// <param name="HostPayoutAmount">O que o anfitrião recebeu da parcela (90% do retido), depois do repasse.</param>
/// <param name="DisputeStatus"><c>none</c>, <c>open</c>, <c>won</c> ou <c>lost</c>.</param>
/// <param name="Stripe">Os ids no Stripe, para conferir no Dashboard — só para o admin.</param>
public sealed record InstallmentResponse(
    int Number,
    decimal Amount,
    DateTimeOffset DueAt,
    string Status,
    string? Method,
    DateTimeOffset? PaidAt,
    string? FailureCode,
    string RefundStatus,
    decimal? RefundedAmount,
    DateTimeOffset? RefundedAt,
    string PayoutStatus,
    decimal? HostPayoutAmount,
    DateTimeOffset? TransferredAt,
    string DisputeStatus,
    InstallmentStripeIds? Stripe);

/// <summary>Os objetos da parcela no Stripe.</summary>
public sealed record InstallmentStripeIds(
    string? PaymentIntentId,
    string? ChargeId,
    string? RefundId,
    string? TransferId,
    string? TransferReversalId,
    string? DisputeId,
    string? PayoutFailureCode);

/// <summary>Uma forma de parcelar.</summary>
/// <param name="Count">Número de parcelas (1 = à vista).</param>
/// <param name="FirstAmount">Valor da 1ª parcela (leva os centavos que sobram).</param>
/// <param name="OtherAmount">Valor de cada uma das outras.</param>
public sealed record InstallmentOptionResponse(int Count, decimal FirstAmount, decimal OtherAmount);

/// <summary>O repasse ao anfitrião, somando as parcelas.</summary>
/// <param name="Transferred">Já está na conta do anfitrião (menos o que foi estornado).</param>
/// <param name="Held">Pago pelo tutor e guardado: vai para o anfitrião no check-out (90%).</param>
/// <param name="Reversed">Estornado por contestação perdida.</param>
public sealed record PayoutSummaryResponse(decimal Transferred, decimal Held, decimal Reversed);
