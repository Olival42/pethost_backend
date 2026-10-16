namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>O que mudou numa parcela ao aplicar a releitura do Stripe (para trilha e avisos).</summary>
/// <param name="BecamePaid">A parcela acabou de ser paga.</param>
/// <param name="DisputeOpened">O tutor acabou de contestar.</param>
/// <param name="DisputeClosed">A contestação acabou de ser decidida (<see cref="Dispute"/> diz como).</param>
/// <param name="RefundFailed">O reembolso acabou de falhar.</param>
public sealed record InstallmentChange(
    int Number,
    bool BecamePaid,
    bool DisputeOpened,
    bool DisputeClosed,
    DisputeStatus Dispute,
    bool RefundFailed);
