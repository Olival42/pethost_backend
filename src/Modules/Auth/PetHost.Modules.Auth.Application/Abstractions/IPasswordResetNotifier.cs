namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>
/// Avisa a pessoa do token de troca de senha. A implementação manda e-mail; a
/// Application não sabe de SMTP nem do texto da mensagem.
/// </summary>
public interface IPasswordResetNotifier
{
    Task NotifyAsync(PasswordResetNotification notification, CancellationToken cancellationToken);
}

public sealed record PasswordResetNotification(
    string Email,
    string FullName,
    string Token,
    DateTimeOffset ExpiresAt);
