namespace PetHost.Shared.Infrastructure.Email;

/// <summary>
/// Servidor SMTP e remetente. Vem do ambiente como <c>Email__Host</c>,
/// <c>Email__Port</c>, <c>Email__Username</c>, <c>Email__Password</c>,
/// <c>Email__FromAddress</c> e <c>Email__FromName</c>.
/// </summary>
/// <remarks>
/// Em desenvolvimento o compose aponta para o Mailpit, que captura tudo sem
/// entregar a ninguém. TLS é negociado sozinho (STARTTLS quando o servidor oferece).
/// </remarks>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    /// <summary>Vazio = servidor sem autenticação (caso do Mailpit).</summary>
    public string? Username { get; init; }

    /// <summary>Nunca versionado.</summary>
    public string? Password { get; init; }

    public string FromAddress { get; init; } = string.Empty;

    public string FromName { get; init; } = "PetHost";
}
