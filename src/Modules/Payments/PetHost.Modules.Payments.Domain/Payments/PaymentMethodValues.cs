namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Valores de <see cref="PaymentMethod"/> no banco e no JSON.</summary>
public static class PaymentMethodValues
{
    private static readonly WireValues<PaymentMethod> Wire = new(new Dictionary<PaymentMethod, string>
    {
        [PaymentMethod.Card] = "card",
        [PaymentMethod.Pix] = "pix",
    });

    /// <summary>Todos os valores aceitos.</summary>
    public static IReadOnlyList<string> All => Wire.All;

    public static string ToWire(PaymentMethod value) => Wire.ToWire(value);

    public static PaymentMethod FromWire(string value) => Wire.FromWire(value);
}
