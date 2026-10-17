namespace PetHost.Shared.Infrastructure.Cors;

/// <summary>
/// De onde o navegador pode chamar a API (seção <c>Cors</c>): a origem do front em Next.js
/// (<c>http://localhost:3000</c> no desenvolvimento) e, em produção, o domínio publicado.
/// </summary>
public sealed class FrontendCorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Teto do cache do preflight no navegador. Os navegadores cortam acima de 2h (Chromium) ou 24h (Firefox).</summary>
    public const int MaxPreflightMaxAgeMinutes = 120;

    /// <summary>
    /// Origens aceitas, só esquema, host e porta (<c>https://app.pethost.com.br</c>), sem caminho
    /// nem barra no fim. Vazio: nenhum navegador de outra origem chama a API. <c>*</c> não é aceito,
    /// porque a política manda credenciais (o SignalR negocia com <c>withCredentials</c>).
    /// </summary>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>Por quantos minutos o navegador guarda a resposta do preflight (<c>OPTIONS</c>). Padrão 10.</summary>
    public int PreflightMaxAgeMinutes { get; set; } = 10;
}
