using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Sessions.SwitchSession;

/// <summary>
/// Abre uma sessão na outra conta da mesma pessoa. A sessão atual não é encerrada:
/// o front descarta os tokens antigos ou guarda os dois para alternar rápido.
/// </summary>
public sealed class SwitchSessionCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenStore refreshTokenStore,
    IAuditTrail auditTrail) : ICommandHandler<SwitchSessionCommand, SessionResponse>
{
    public async Task<Result<SessionResponse>> HandleAsync(
        SwitchSessionCommand command,
        CancellationToken cancellationToken)
    {
        var id = new UserId(command.UserId);
        var current = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (current is null)
            return Result<SessionResponse>.Failure(AuthErrors.UserNotFound(id));

        if (!UserRoleValues.TryParse(command.Role, out var targetRole) || targetRole == current.Role)
            return Result<SessionResponse>.Failure(AuthErrors.LinkedAccountNotFound);

        var target = await UserCredentials
            .FindAsync(userRepository, passwordHasher, current.Email, targetRole, command.Password, cancellationToken)
            .ConfigureAwait(false);

        // Sem conta, senha errada, conta inativa ou suspensa: a mesma resposta.
        if (target is null || !target.IsActive || target.IsSuspended)
            return Result<SessionResponse>.Failure(AuthErrors.LinkedAccountNotFound);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.SessionSwitched,
                    AuditTargets.Account,
                    target.Id.Value,
                    Details: new Dictionary<string, string?>
                    {
                        ["fromAccountId"] = current.Id.Value.ToString(),
                        ["toRole"] = UserRoleValues.ToWire(target.Role),
                    },
                    ActorId: current.Id.Value),
                cancellationToken)
            .ConfigureAwait(false);

        var accessToken = accessTokenGenerator.Generate(target);
        var refreshToken = await refreshTokenStore
            .IssueAsync(target.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result<SessionResponse>.Success(
            SessionResponseFactory.Create(target, accessToken, refreshToken));
    }
}
