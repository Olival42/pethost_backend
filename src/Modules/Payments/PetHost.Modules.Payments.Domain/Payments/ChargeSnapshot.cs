namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>
/// A cobrança de uma parcela como o Stripe a vê agora (relida na API depois de um webhook): é o
/// que o agregado aplica (<see cref="Payment.ApplyCharge"/>). Ler de novo e aplicar torna o webhook
/// idempotente e imune a evento repetido ou fora de ordem.
/// </summary>
/// <param name="Method">Cartão ou Pix, quando o tutor já escolheu.</param>
/// <param name="ChargeId">A cobrança (<c>ch_...</c>), origem do repasse.</param>
/// <param name="FailureCode">Por que a última tentativa falhou (código do Stripe), se falhou.</param>
/// <param name="Refund">O reembolso, se houver.</param>
/// <param name="Dispute">A contestação, se houver.</param>
public sealed record ChargeSnapshot(
    ChargeState State,
    PaymentMethod? Method = null,
    string? ChargeId = null,
    string? FailureCode = null,
    RefundSnapshot? Refund = null,
    DisputeSnapshot? Dispute = null);
