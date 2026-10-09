using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Users.SuspendAccount;

/// <summary>
/// Suspende a conta e derruba todas as sessões dela na hora. A partir daí login,
/// refresh, troca de conta, reativação e "esqueci a senha" não funcionam para ela.
/// </summary>
public sealed class SuspendAccountCommandHandler(
    IUserRepository userRepository,
    IAuthUnitOfWork unitOfWork,
    IRefreshTokenStore refreshTokenStore,
    IAccessTokenRevocationStore accessTokenRevocationStore,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<SuspendAccountCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SuspendAccountCommand command, CancellationToken cancellationToken)
    {
        var id = new UserId(command.UserId);
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Result<Unit>.Failure(AuthErrors.UserNotFound(id));

        var wasSuspended = user.IsSuspended;
        var suspended = user.Suspend(command.Reason, command.AdminId, timeProvider.GetUtcNow());
        if (suspended.IsFailure)
            return Result<Unit>.FromFailure(suspended);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await refreshTokenStore.RevokeAllAsync(id, cancellationToken).ConfigureAwait(false);
        await accessTokenRevocationStore.RevokeAllAsync(id, cancellationToken).ConfigureAwait(false);

        if (!wasSuspended)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.AccountSuspended, AuditTargets.Account, id.Value, command.Reason), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}

/// <summary>Tira a suspensão. A conta volta ao status (ativa/inativa) que tinha.</summary>
public sealed class LiftAccountSuspensionCommandHandler(
    IUserRepository userRepository,
    IAuthUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<LiftAccountSuspensionCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LiftAccountSuspensionCommand command, CancellationToken cancellationToken)
    {
        var id = new UserId(command.UserId);
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Result<Unit>.Failure(AuthErrors.UserNotFound(id));

        if (!user.IsSuspended)
            return Result<Unit>.Success(Unit.Value);

        user.LiftSuspension(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.AccountSuspensionLifted, AuditTargets.Account, id.Value), cancellationToken)
            .ConfigureAwait(false);

        return Result<Unit>.Success(Unit.Value);
    }
}
