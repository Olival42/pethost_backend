namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Valores de <see cref="IdentityDocumentType"/> no banco e no JSON.</summary>
public static class IdentityDocumentTypeValues
{
    private static readonly WireValues<IdentityDocumentType> Values = new(new Dictionary<IdentityDocumentType, string>
    {
        [IdentityDocumentType.Rg] = "rg",
        [IdentityDocumentType.Cnh] = "cnh",
        [IdentityDocumentType.Cin] = "cin",
    });

    public static IReadOnlyList<string> All => Values.All;

    public static string ToWire(IdentityDocumentType type) => Values.ToWire(type);

    public static IdentityDocumentType FromWire(string value) => Values.FromWire(value);

    public static bool TryParse(string? value, out IdentityDocumentType type) => Values.TryParse(value, out type);
}
