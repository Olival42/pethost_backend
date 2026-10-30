namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Valores de <see cref="DisputeStatus"/> no banco e no JSON.</summary>
public static class DisputeStatusValues
{
    private static readonly WireValues<DisputeStatus> Wire = new(new Dictionary<DisputeStatus, string>
    {
        [DisputeStatus.None] = "none",
        [DisputeStatus.Open] = "open",
        [DisputeStatus.Won] = "won",
        [DisputeStatus.Lost] = "lost",
    });

    /// <summary>Todos os valores aceitos.</summary>
    public static IReadOnlyList<string> All => Wire.All;

    public static string ToWire(DisputeStatus value) => Wire.ToWire(value);

    public static DisputeStatus FromWire(string value) => Wire.FromWire(value);
}
