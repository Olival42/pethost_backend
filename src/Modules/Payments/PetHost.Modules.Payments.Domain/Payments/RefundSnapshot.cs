namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>O reembolso de uma parcela no Stripe.</summary>
/// <param name="AmountCents">Quanto foi (ou está sendo) devolvido.</param>
public sealed record RefundSnapshot(string Id, long AmountCents, RefundStatus Status);
