namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>
/// Avisa a pessoa de que a senha da conta foi trocada, para ela reagir se não foi ela.
/// A implementação manda e-mail.
/// </summary>
public interface IPasswordChangedNotifier
{
    Task NotifyAsync(PasswordChangedNotification notification, CancellationToken cancellationToken);
}

public sealed record PasswordChangedNotification(
    string Email,
    string FullName,
    DateTimeOffset ChangedAt);
