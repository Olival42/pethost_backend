namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Valores de <see cref="InstallmentStatus"/> no banco e no JSON.</summary>
public static class InstallmentStatusValues
{
    private static readonly WireValues<InstallmentStatus> Wire = new(new Dictionary<InstallmentStatus, string>
    {
        [InstallmentStatus.Pending] = "pending",
        [InstallmentStatus.Processing] = "processing",
        [InstallmentStatus.Paid] = "paid",
        [InstallmentStatus.Overdue] = "overdue",
        [InstallmentStatus.Cancelled] = "cancelled",
    });

    /// <summary>Todos os valores aceitos.</summary>
    public static IReadOnlyList<string> All => Wire.All;

    public static string ToWire(InstallmentStatus value) => Wire.ToWire(value);

    public static InstallmentStatus FromWire(string value) => Wire.FromWire(value);
}
