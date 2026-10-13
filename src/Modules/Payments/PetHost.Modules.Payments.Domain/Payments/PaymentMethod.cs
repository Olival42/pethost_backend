namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Como o tutor pagou uma parcela, dentro do PetHost (checkout transparente).</summary>
public enum PaymentMethod
{
    /// <summary>Cartão.</summary>
    Card,

    /// <summary>Pix (QR code na própria página).</summary>
    Pix,
}
