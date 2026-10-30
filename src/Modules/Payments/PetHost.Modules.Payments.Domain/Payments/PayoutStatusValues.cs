namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Valores de <see cref="PayoutStatus"/> no banco e no JSON.</summary>
public static class PayoutStatusValues
{
    private static readonly WireValues<PayoutStatus> Wire = new(new Dictionary<PayoutStatus, string>
    {
        [PayoutStatus.Held] = "held",
        [PayoutStatus.Transferred] = "transferred",
        [PayoutStatus.Cancelled] = "cancelled",
        [PayoutStatus.Reversed] = "reversed",
    });

    /// <summary>Todos os valores aceitos.</summary>
    public static IReadOnlyList<string> All => Wire.All;

    public static string ToWire(PayoutStatus value) => Wire.ToWire(value);

    public static PayoutStatus FromWire(string value) => Wire.FromWire(value);
}
