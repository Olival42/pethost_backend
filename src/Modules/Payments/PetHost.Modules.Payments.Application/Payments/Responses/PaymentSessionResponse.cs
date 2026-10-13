namespace PetHost.Modules.Payments.Application.Payments.Responses;

/// <summary>
/// O que o app precisa para o checkout transparente de uma parcela: abrir o Payment Element do
/// Stripe (<c>stripe.elements({ clientSecret })</c>) com cartão e Pix, sem sair do PetHost.
/// </summary>
/// <param name="ClientSecret">Segredo da cobrança, só do tutor dono dela. Não guarde.</param>
/// <param name="PublishableKey">Chave publicável do Stripe (<c>pk_test_...</c>).</param>
/// <param name="PixExpiresAt">Até quando o QR code do Pix vale (Unix, segundos, UTC).</param>
/// <param name="PaymentMethods">Os meios aceitos: <c>card</c> e <c>pix</c>.</param>
public sealed record PaymentSessionResponse(
    Guid BookingId,
    int InstallmentNumber,
    int InstallmentCount,
    decimal Amount,
    string PaymentIntentId,
    string ClientSecret,
    string PublishableKey,
    long PixExpiresAt,
    IReadOnlyList<string> PaymentMethods);
