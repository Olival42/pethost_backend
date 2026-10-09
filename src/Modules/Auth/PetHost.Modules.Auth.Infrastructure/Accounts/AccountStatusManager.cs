using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Users.SuspendAccount;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Infrastructure.Accounts;

/// <summary>
/// Adaptador do contrato <see cref="IAccountStatusManager"/> (§5). Inativar e reativar
/// vão direto ao agregado; suspender usa os handlers do admin.
/// </summary>
internal sealed class AccountStatusManager(
    IUserRepository userRepository,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenStore refreshTokenStore,
    IAccessTokenRevocationStore accessTokenRevocationStore,
    ICommandHandler<SuspendAccountCommand, Unit> suspendAccount,
    ICommandHandler<LiftAccountSuspensionCommand, Unit> liftAccountSuspension,
    IUserDirectory userDirectory,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IAccountStatusManager
{
    public async Task<Result> DeactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var id = new UserId(userId);
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Result.Failure(AuthErrors.UserNotFound(id));

        var wasActive = user.IsActive;
        var deactivated = user.Deactivate(timeProvider.GetUtcNow());
        if (deactivated.IsFailure)
            return deactivated;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await refreshTokenStore.RevokeAllAsync(id, cancellationToken).ConfigureAwait(false);
        await accessTokenRevocationStore.RevokeAllAsync(id, cancellationToken).ConfigureAwait(false);

        if (wasActive)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.AccountDeactivated, AuditTargets.Account, id.Value, ActorId: id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Success();
    }

    public async Task<Result<AccountSession>> ReactivateAsync(
        string? email,
        string? password,
        string role,
        CancellationToken cancellationToken)
    {
        var parsedEmail = Email.Create(email);
        if (parsedEmail.IsFailure || !UserRoleValues.TryParse(role, out var parsedRole))
            return Result<AccountSession>.Failure(AuthErrors.InvalidCredentials);

        // Mesma conferência do login: credencial errada não diz se a conta existe.
        var user = await UserCredentials
            .FindAsync(userRepository, passwordHasher, parsedEmail.Value!, parsedRole, password, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
            return Result<AccountSession>.Failure(AuthErrors.InvalidCredentials);

        if (user.IsSuspended)
            return Result<AccountSession>.Failure(AuthErrors.AccountSuspended);

        var wasActive = user.IsActive;
        user.Reactivate(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!wasActive)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.AccountReactivated, AuditTargets.Account, user.Id.Value, ActorId: user.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        var accessToken = accessTokenGenerator.Generate(user);
        var refreshToken = await refreshTokenStore.IssueAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var accounts = await userDirectory
            .GetByIdsAsync([user.Id.Value], cancellationToken)
            .ConfigureAwait(false);

        return Result<AccountSession>.Success(new AccountSession(
            accessToken.Value,
            refreshToken.Value,
            accessToken.ExpiresAt.ToUnixTimeSeconds(),
            accounts[0]));
    }

    public async Task<Result> SuspendAsync(Guid userId, string? reason, Guid adminId, CancellationToken cancellationToken)
    {
        var result = await suspendAccount
            .HandleAsync(new SuspendAccountCommand(userId, reason, adminId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? Result.Failure(result.Errors!) : Result.Success();
    }

    public async Task<Result> LiftSuspensionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await liftAccountSuspension
            .HandleAsync(new LiftAccountSuspensionCommand(userId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? Result.Failure(result.Errors!) : Result.Success();
    }
}
