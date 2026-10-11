namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Valores de <see cref="RejectionReason"/> no banco e no JSON.</summary>
public static class RejectionReasonValues
{
    private static readonly WireValues<RejectionReason> Values = new(new Dictionary<RejectionReason, string>
    {
        [RejectionReason.DocumentIllegible] = "document_illegible",
        [RejectionReason.DocumentExpired] = "document_expired",
        [RejectionReason.SelfieMismatch] = "selfie_mismatch",
        [RejectionReason.DataMismatch] = "data_mismatch",
        [RejectionReason.MissingDocument] = "missing_document",
        [RejectionReason.SuspectedFraud] = "suspected_fraud",
        [RejectionReason.Other] = "other",
    });

    public static IReadOnlyList<string> All => Values.All;

    public static string ToWire(RejectionReason reason) => Values.ToWire(reason);

    public static RejectionReason FromWire(string value) => Values.FromWire(value);

    public static bool TryParse(string? value, out RejectionReason reason) => Values.TryParse(value, out reason);
}
