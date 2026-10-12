namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// O que o Stripe diz da conta conectada num dado momento (objeto <c>Account</c>): só o que o
/// PetHost precisa para decidir se o anfitrião pode receber.
/// </summary>
/// <param name="DetailsSubmitted">A pessoa terminou o onboarding (<c>details_submitted</c>).</param>
/// <param name="ChargesEnabled">Pode receber cobranças (<c>charges_enabled</c>).</param>
/// <param name="PayoutsEnabled">Pode receber repasses (<c>payouts_enabled</c>).</param>
/// <param name="DisabledReason">Por que está bloqueada (<c>requirements.disabled_reason</c>).</param>
public sealed record StripeAccountSnapshot(
    bool DetailsSubmitted,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    string? DisabledReason)
{
    /// <summary>Limite de <c>hosts.stripe_disabled_reason</c>.</summary>
    public const int DisabledReasonMaxLength = 100;

    /// <summary>
    /// Motivos em que o Stripe só está conferindo — não falta nada da pessoa. Os outros
    /// (<c>requirements.past_due</c>, <c>rejected.*</c>...) pedem ação ou são definitivos.
    /// </summary>
    internal static readonly string[] ReviewReasons = ["requirements.pending_verification", "under_review"];

    /// <summary>Prefixo dos motivos de recusa definitiva (<c>rejected.fraud</c>, <c>rejected.terms_of_service</c>...).</summary>
    public const string RejectionPrefix = "rejected.";

    /// <summary>O Stripe recusou a conta de vez (não é algo que a pessoa resolve sozinha).</summary>
    public static bool IsRejection(string? disabledReason) =>
        disabledReason is not null && disabledReason.StartsWith(RejectionPrefix, StringComparison.Ordinal);

    /// <summary>O motivo sem espaços nas pontas e cortado no tamanho da coluna; vazio vira nulo.</summary>
    public string? NormalizedDisabledReason =>
        string.IsNullOrWhiteSpace(DisabledReason)
            ? null
            : DisabledReason.Trim() is var reason && reason.Length > DisabledReasonMaxLength
                ? reason[..DisabledReasonMaxLength]
                : DisabledReason.Trim();
}
