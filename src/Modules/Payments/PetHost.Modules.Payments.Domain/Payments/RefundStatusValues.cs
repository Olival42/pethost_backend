namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Valores de <see cref="RefundStatus"/> no banco e no JSON.</summary>
public static class RefundStatusValues
{
    private static readonly WireValues<RefundStatus> Wire = new(new Dictionary<RefundStatus, string>
    {
        [RefundStatus.None] = "none",
        [RefundStatus.Pending] = "pending",
        [RefundStatus.Succeeded] = "succeeded",
        [RefundStatus.Failed] = "failed",
    });

    /// <summary>Todos os valores aceitos.</summary>
    public static IReadOnlyList<string> All => Wire.All;

    public static string ToWire(RefundStatus value) => Wire.ToWire(value);

    public static RefundStatus FromWire(string value) => Wire.FromWire(value);
}
