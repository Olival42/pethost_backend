namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Valores de <see cref="HostApplicationStatus"/> no banco e no JSON.</summary>
public static class HostApplicationStatusValues
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string RejectedFinal = "rejected_final";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    private static readonly WireValues<HostApplicationStatus> Values = new(new Dictionary<HostApplicationStatus, string>
    {
        [HostApplicationStatus.Draft] = Draft,
        [HostApplicationStatus.Submitted] = Submitted,
        [HostApplicationStatus.Approved] = Approved,
        [HostApplicationStatus.Rejected] = Rejected,
        [HostApplicationStatus.RejectedFinal] = RejectedFinal,
        [HostApplicationStatus.Cancelled] = Cancelled,
        [HostApplicationStatus.Expired] = Expired,
    });

    public static IReadOnlyList<string> All => Values.All;

    public static string ToWire(HostApplicationStatus status) => Values.ToWire(status);

    public static HostApplicationStatus FromWire(string value) => Values.FromWire(value);

    public static bool TryParse(string? value, out HostApplicationStatus status) => Values.TryParse(value, out status);
}
