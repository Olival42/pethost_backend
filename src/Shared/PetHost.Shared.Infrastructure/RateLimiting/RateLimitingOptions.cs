namespace PetHost.Shared.Infrastructure.RateLimiting;

/// <summary>
/// Limites de requisição, seção <c>RateLimiting</c>. Os valores padrão já servem para
/// produção; a configuração só precisa mudar o que quiser ajustar, por exemplo
/// <c>RateLimiting__Policies__credentials__PermitLimit=20</c>.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Desligado, nenhum limite é aplicado (os testes de integração usam isso).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Limite geral, por usuário logado ou por IP: vale para todo endpoint.</summary>
    public RateLimitRule Global { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    /// <summary>Limites das políticas de <see cref="RateLimitPolicies"/>, pelo nome.</summary>
    public Dictionary<string, RateLimitRule> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // Força bruta de senha: 10 tentativas a cada 5 minutos por IP.
        [RateLimitPolicies.Credentials] = new() { PermitLimit = 10, WindowSeconds = 300 },

        // Criação de contas em massa.
        [RateLimitPolicies.Registration] = new() { PermitLimit = 10, WindowSeconds = 3600 },

        // Cada pedido manda um e-mail: protege a caixa da pessoa e a reputação do remetente.
        [RateLimitPolicies.PasswordReset] = new() { PermitLimit = 5, WindowSeconds = 900 },

        [RateLimitPolicies.Session] = new() { PermitLimit = 30, WindowSeconds = 60 },

        // Pede a senha atual: mesmo cuidado do login, mas por conta.
        [RateLimitPolicies.AccountSensitive] = new() { PermitLimit = 5, WindowSeconds = 900 },

        // Troca de CPF (que pede a senha) passa por aqui também.
        [RateLimitPolicies.AccountUpdate] = new() { PermitLimit = 20, WindowSeconds = 900 },
        // Chat: uma conversa animada fica em 10 a 15 mensagens por minuto, e 30 ainda cabe uma
        // sequência de fotos do pet. Acima disso é robô ou spam (uma mensagem a cada 2 s, sem parar).
        [RateLimitPolicies.ChatMessages] = new() { PermitLimit = 30, WindowSeconds = 60 },

        // Avaliação: uma por lado por reserva, então quem escreve, ajusta e responde algumas
        // estadias cabe folgado em 20 a cada 15 minutos.
        [RateLimitPolicies.ReviewWrites] = new() { PermitLimit = 20, WindowSeconds = 900 },

        // Denúncia: cada uma manda e-mail ao admin. 10 por hora é muito para uso honesto.
        [RateLimitPolicies.Reports] = new() { PermitLimit = 10, WindowSeconds = 3600 },
    };
}

/// <summary>Quantas requisições cabem numa janela fixa.</summary>
public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}
