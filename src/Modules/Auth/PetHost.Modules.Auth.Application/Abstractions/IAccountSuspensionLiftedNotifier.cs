using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>
/// Avisa a pessoa de que o admin tirou a suspensão da conta, para ela saber que pode
/// voltar a entrar. A implementação manda e-mail.
/// </summary>
public interface IAccountSuspensionLiftedNotifier
{
    Task NotifyAsync(AccountSuspensionLiftedNotification notification, CancellationToken cancellationToken);
}

/// <param name="Role">Qual das contas do e-mail (tutor ou anfitrião) voltou.</param>
/// <param name="IsActive">A conta voltou ativa? Inativa, a pessoa reativa com a senha.</param>
public sealed record AccountSuspensionLiftedNotification(
    string Email,
    string FullName,
    UserRole Role,
    bool IsActive,
    DateTimeOffset LiftedAt);
