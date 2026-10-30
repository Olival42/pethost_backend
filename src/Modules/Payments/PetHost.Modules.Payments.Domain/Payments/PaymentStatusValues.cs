namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Valores de <see cref="PaymentStatus"/> no banco e no JSON.</summary>
public static class PaymentStatusValues
{
    private static readonly WireValues<PaymentStatus> Wire = new(new Dictionary<PaymentStatus, string>
    {
        [PaymentStatus.Pending] = "pending",
        [PaymentStatus.Active] = "active",
        [PaymentStatus.Paid] = "paid",
        [PaymentStatus.Cancelled] = "cancelled",
    });

    /// <summary>Todos os valores aceitos.</summary>
    public static IReadOnlyList<string> All => Wire.All;

    public static string ToWire(PaymentStatus value) => Wire.ToWire(value);

    public static PaymentStatus FromWire(string value) => Wire.FromWire(value);
}
