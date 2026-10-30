using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Passwords.ForgotPassword;

/// <summary>
/// Emite o token de troca de senha e manda por e-mail. Responde <b>sucesso sempre</b>,
/// exista a conta ou não: de fora não dá para usar este endpoint para descobrir quem
/// tem cadastro.
/// </summary>
public sealed class ForgotPasswordCommandHandler(
    IUserRepository userRepository,
    IPasswordResetTokenStore passwordResetTokenStore,
    IPasswordResetNotifier passwordResetNotifier,
    IAuditTrail auditTrail) : ICommandHandler<ForgotPasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);
        if (email.IsFailure || !UserRoleValues.TryParse(command.Role, out var role))
            return Result<Unit>.Success(Unit.Value);

        var user = await userRepository
            .GetByEmailAndRoleAsync(email.Value!, role, cancellationToken)
            .ConfigureAwait(false);

        // Conta suspensa não ganha token: trocar a senha não a deixaria entrar. A resposta
        // é a mesma de sempre, para não revelar a suspensão.
        if (user is null || user.IsSuspended)
            return Result<Unit>.Success(Unit.Value);

        var token = await passwordResetTokenStore
            .IssueAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        // O notifier só enfileira: o SMTP roda em segundo plano, então o tempo de
        // resposta não denuncia que houve envio.
        await passwordResetNotifier
            .NotifyAsync(
                new PasswordResetNotification(user.Email.Value, user.FullName.Value, token.Value, token.ExpiresAt),
                cancellationToken)
            .ConfigureAwait(false);

        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.PasswordResetRequested, AuditTargets.Account, user.Id.Value), cancellationToken)
            .ConfigureAwait(false);

        return Result<Unit>.Success(Unit.Value);
    }
}
