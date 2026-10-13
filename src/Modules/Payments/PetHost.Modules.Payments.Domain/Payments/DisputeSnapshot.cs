namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>A contestação de uma parcela no Stripe (aberta, ganha ou perdida).</summary>
public sealed record DisputeSnapshot(string Id, DisputeStatus Status);
