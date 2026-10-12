namespace PetHost.Shared.Infrastructure.Stripe;

/// <summary>
/// Conta da plataforma no Stripe. Vem do ambiente como <c>Stripe__SecretKey</c> etc. Sempre em
/// <b>modo de teste</b> (<c>sk_test_...</c>): nenhum dinheiro de verdade.
/// </summary>
/// <remarks>
/// Sem <see cref="SecretKey"/> a API sobe normalmente, mas tudo o que fala com o Stripe responde
/// <c>503 ..._UNAVAILABLE</c> (desenvolvimento sem conta Stripe, testes).
/// </remarks>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Chave secreta da plataforma (<c>sk_test_...</c>). Nunca versionada.</summary>
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// Chave publicável (<c>pk_test_...</c>). Vai para o app junto com o <c>clientSecret</c> do
    /// onboarding embutido: o componente do Stripe precisa das duas.
    /// </summary>
    public string PublishableKey { get; init; } = string.Empty;

    /// <summary>
    /// Segredo do webhook (<c>whsec_...</c>) do destino "eventos da sua conta". A assinatura de
    /// todo webhook é conferida com ele.
    /// </summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>
    /// Segredo do destino "eventos das contas conectadas" (onde chega o <c>account.updated</c>),
    /// quando é um destino separado no Dashboard. Vazio = vale o <see cref="WebhookSecret"/> (é o
    /// caso da Stripe CLI, que usa um segredo só).
    /// </summary>
    public string? ConnectWebhookSecret { get; init; }

    /// <summary>
    /// Para onde o Stripe devolve o anfitrião ao terminar o onboarding hospedado (link). Fixo na
    /// configuração, nunca vindo do cliente: senão viraria um redirecionamento aberto.
    /// </summary>
    public string ConnectReturnUrl { get; init; } = "http://localhost:3000/anfitriao/recebimentos?stripe=retorno";

    /// <summary>Para onde o Stripe manda quando o link venceu ou já foi usado: o app pede um link novo.</summary>
    public string ConnectRefreshUrl { get; init; } = "http://localhost:3000/anfitriao/recebimentos?stripe=renovar";

    /// <summary>
    /// No modo de teste, troca no cadastro de recebimento os dados que o Stripe confere (nascimento,
    /// endereço) pelos valores de teste dele e já anexa o documento de teste aprovado: a conta
    /// conectada é aprovada sem upload. Padrão <c>true</c>; nunca vale com chave de produção.
    /// </summary>
    public bool TestOnboardingData { get; init; } = true;

    /// <summary>Chave de teste (<c>sk_test_...</c> ou restrita <c>rk_test_...</c>).</summary>
    public bool IsTestMode =>
        SecretKey.Trim().StartsWith("sk_test_", StringComparison.Ordinal)
        || SecretKey.Trim().StartsWith("rk_test_", StringComparison.Ordinal);

    /// <summary>Usa os valores de teste do Stripe no cadastro de recebimento (só no modo de teste).</summary>
    public bool UsesTestOnboardingData => TestOnboardingData && IsTestMode;

    /// <summary>Há chave secreta: dá para chamar a API do Stripe.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>Os segredos de webhook configurados, sem repetição.</summary>
    public IReadOnlyList<string> WebhookSecrets =>
        [.. new[] { WebhookSecret, ConnectWebhookSecret }
            .Where(secret => !string.IsNullOrWhiteSpace(secret))
            .Select(secret => secret!.Trim())
            .Distinct(StringComparer.Ordinal)];
}
