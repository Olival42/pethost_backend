namespace PetHost.Api.Support;

/// <summary>Contato do suporte (seção <c>Support</c>; no ambiente, <c>Support__Email</c>).</summary>
public sealed class SupportOptions
{
    public const string SectionName = "Support";

    /// <summary>
    /// E-mail que o app mostra quando a API manda falar com o suporte (CPF já usado por outra conta,
    /// identidade travada, recusa do Stripe...). Vazio = a rota responde sem e-mail.
    /// </summary>
    public string? Email { get; init; }
}
