namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Identidade de <see cref="Payment"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct PaymentId(Guid Value)
{
    public static PaymentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
