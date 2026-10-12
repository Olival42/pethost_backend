namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>Valores de <see cref="StripeOnboardingStatus"/> no JSON.</summary>
public static class StripeOnboardingStatusValues
{
    public const string NotStarted = "not_started";
    public const string InProgress = "in_progress";
    public const string UnderReview = "under_review";
    public const string Restricted = "restricted";
    public const string Rejected = "rejected";
    public const string Enabled = "enabled";

    public static readonly IReadOnlyList<string> All = [NotStarted, InProgress, UnderReview, Restricted, Rejected, Enabled];

    public static string ToWire(StripeOnboardingStatus status) => status switch
    {
        StripeOnboardingStatus.NotStarted => NotStarted,
        StripeOnboardingStatus.InProgress => InProgress,
        StripeOnboardingStatus.UnderReview => UnderReview,
        StripeOnboardingStatus.Restricted => Restricted,
        StripeOnboardingStatus.Rejected => Rejected,
        StripeOnboardingStatus.Enabled => Enabled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown Stripe onboarding status."),
    };

    /// <summary>Lê o valor do JSON/query (maiúsculas valem). <c>false</c> se não existir.</summary>
    public static bool TryFromWire(string? value, out StripeOnboardingStatus status)
    {
        foreach (var candidate in Enum.GetValues<StripeOnboardingStatus>())
        {
            if (string.Equals(ToWire(candidate), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }
}
