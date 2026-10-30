using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Sessions.CreateSession;

/// <summary>
/// Autentica o par (e-mail, role) e emite a sessão. Qualquer falha devolve o
/// mesmo <c>AUTH_INVALID_CREDENTIALS</c>: e-mail inexistente e senha errada são
/// indistinguíveis de fora.
/// </summary>
public sealed class CreateSessionCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenStore refreshTokenStore,
    IAuditTrail auditTrail) : ICommandHandler<CreateSessionCommand, SessionResponse>
{
    public async Task<Result<SessionResponse>> HandleAsync(
        CreateSessionCommand command,
        CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);
        if (email.IsFailure)
            return Result<SessionResponse>.Failure(AuthErrors.InvalidCredentials);

        if (!UserRoleValues.TryParse(command.Role, out var role))
            return Result<SessionResponse>.Failure(AuthErrors.RoleUnknown);

        var user = await UserCredentials
            .FindAsync(userRepository, passwordHasher, email.Value!, role, command.Password, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            // Para a trilha: a conta existe e a senha estava errada, ou o e-mail nem existe?
            var target = await userRepository.GetByEmailAndRoleAsync(email.Value!, role, cancellationToken).ConfigureAwait(false);
            await RecordFailureAsync(target?.Id.Value, role, "invalid_credentials", cancellationToken).ConfigureAwait(false);
            return Result<SessionResponse>.Failure(AuthErrors.InvalidCredentials);
        }

        // Só depois da senha conferida: "conta suspensa/inativa" não pode virar oráculo
        // de quem tem cadastro. Suspensa vem antes: reativar não resolve.
        if (user.IsSuspended)
        {
            await RecordFailureAsync(user.Id.Value, role, "suspended", cancellationToken).ConfigureAwait(false);
            return Result<SessionResponse>.Failure(AuthErrors.AccountSuspended);
        }

        if (!user.IsActive)
        {
            await RecordFailureAsync(user.Id.Value, role, "deactivated", cancellationToken).ConfigureAwait(false);
            return Result<SessionResponse>.Failure(AuthErrors.AccountDeactivated);
        }

        var accessToken = accessTokenGenerator.Generate(user);
        var refreshToken = await refreshTokenStore
            .IssueAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.LoginSucceeded,
                    AuditTargets.Account,
                    user.Id.Value,
                    Details: new Dictionary<string, string?> { ["role"] = UserRoleValues.ToWire(role) },
                    ActorId: user.Id.Value),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<SessionResponse>.Success(
            SessionResponseFactory.Create(user, accessToken, refreshToken));
    }

    /// <summary>
    /// Login recusado. Sem o e-mail digitado (dado pessoal de quem talvez nem tenha
    /// conta): a conta, quando existe, e o IP do request bastam para investigar.
    /// </summary>
    private Task RecordFailureAsync(Guid? accountId, UserRole role, string reason, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.LoginFailed,
                AuditTargets.Account,
                accountId,
                Details: new Dictionary<string, string?> { ["role"] = UserRoleValues.ToWire(role), ["reason"] = reason }),
            cancellationToken);
}
