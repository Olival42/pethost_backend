using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Users.ChangePassword;

/// <summary>
/// Confere a senha atual, grava a nova, derruba <b>todas</b> as sessões da conta e abre
/// uma sessão nova para quem trocou. Se alguém tinha a senha antiga, cai na hora. A
/// pessoa recebe um e-mail avisando da troca.
/// </summary>
public sealed class ChangePasswordCommandHandler(
    IUserRepository userRepository,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenStore refreshTokenStore,
    IAccessTokenRevocationStore accessTokenRevocationStore,
    IPasswordChangedNotifier passwordChangedNotifier,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ChangePasswordCommand, SessionResponse>
{
    public async Task<Result<SessionResponse>> HandleAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        var id = new UserId(command.UserId);
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Result<SessionResponse>.Failure(AuthErrors.UserNotFound(id));

        if (user.IsSuspended)
            return Result<SessionResponse>.Failure(AuthErrors.AccountSuspended);

        if (!user.IsActive)
            return Result<SessionResponse>.Failure(AuthErrors.AccountDeactivated);

        if (!passwordHasher.Verify(command.CurrentPassword ?? string.Empty, user.PasswordHash.Value))
            return Result<SessionResponse>.Failure(AuthErrors.CurrentPasswordIncorrect);

        var newPassword = Password.Create(command.NewPassword);
        if (newPassword.IsFailure)
            return Result<SessionResponse>.FromFailure(newPassword);

        var passwordHash = PasswordHash.FromHash(passwordHasher.Hash(newPassword.Value!.Value));
        if (passwordHash.IsFailure)
            return Result<SessionResponse>.FromFailure(passwordHash);

        var now = timeProvider.GetUtcNow();
        var changed = user.ChangePassword(passwordHash.Value!, now);
        if (changed.IsFailure)
            return Result<SessionResponse>.FromFailure(changed);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await refreshTokenStore.RevokeAllAsync(id, cancellationToken).ConfigureAwait(false);
        await accessTokenRevocationStore.RevokeAllAsync(id, cancellationToken).ConfigureAwait(false);

        // Emitida depois da revogação: é a única sessão que sobra.
        var accessToken = accessTokenGenerator.Generate(user);
        var refreshToken = await refreshTokenStore.IssueAsync(id, cancellationToken).ConfigureAwait(false);

        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.PasswordChanged, AuditTargets.Account, id.Value), cancellationToken)
            .ConfigureAwait(false);

        await passwordChangedNotifier
            .NotifyAsync(new PasswordChangedNotification(user.Email.Value, user.FullName.Value, now), cancellationToken)
            .ConfigureAwait(false);

        return Result<SessionResponse>.Success(SessionResponseFactory.Create(user, accessToken, refreshToken));
    }
}
