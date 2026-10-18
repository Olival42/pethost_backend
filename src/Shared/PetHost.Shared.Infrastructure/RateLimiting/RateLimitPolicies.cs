namespace PetHost.Shared.Infrastructure.RateLimiting;

/// <summary>
/// Nomes das políticas de rate limit, usados em <c>[EnableRateLimiting(...)]</c> nos
/// controllers. Além delas, todo endpoint passa pelo limite geral.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Senha conferida sem sessão: login, troca de conta e reativação. Por IP.</summary>
    public const string Credentials = "credentials";

    /// <summary>Criação de conta. Por IP.</summary>
    public const string Registration = "registration";

    /// <summary>Esqueci a senha (pedido e troca com o token do e-mail). Por IP.</summary>
    public const string PasswordReset = "password-reset";

    /// <summary>Refresh e logout. Por IP.</summary>
    public const string Session = "session";

    /// <summary>Ação sensível da conta logada: trocar a senha, inativar. Por usuário.</summary>
    public const string AccountSensitive = "account-sensitive";

    /// <summary>Alteração de perfil (PATCH). Por usuário.</summary>
    public const string AccountUpdate = "account-update";

    /// <summary>Mensagem do chat (texto e foto contam juntos): segura spam e robô. Por usuário.</summary>
    public const string ChatMessages = "chat-messages";

    /// <summary>Escrever, editar e apagar avaliação e resposta (cada uma pode mandar e-mail). Por usuário.</summary>
    public const string ReviewWrites = "review-writes";

    /// <summary>Denúncia (cada uma avisa o admin por e-mail). Por usuário.</summary>
    public const string Reports = "reports";

    public static readonly IReadOnlyList<string> All =
        [Credentials, Registration, PasswordReset, Session, AccountSensitive, AccountUpdate, ChatMessages, ReviewWrites, Reports];
}
