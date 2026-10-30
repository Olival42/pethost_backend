using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Users;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Infrastructure.Accounts;

/// <summary>
/// Adaptador do contrato <see cref="IAccountProfileEditor"/> (§5). A regra do patch fica
/// em <see cref="ProfilePatch"/>; a alteração em si, no agregado.
/// </summary>
internal sealed class AccountProfileEditor(
    IUserRepository userRepository,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IAccountProfileEditor
{
    public async Task<Result> ValidateAsync(Guid userId, AccountProfilePatch patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var id = new UserId(userId);
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Result.Failure(AuthErrors.UserNotFound(id));

        var changes = ProfilePatch.Apply(user, patch, timeProvider.GetUtcNow());

        return changes.IsFailure ? Result.Failure(changes.Errors!) : Result.Success();
    }

    public async Task<Result<AccountProfilePatch>> UpdateAsync(
        Guid userId,
        AccountProfilePatch patch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var id = new UserId(userId);
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Result<AccountProfilePatch>.Failure(AuthErrors.UserNotFound(id));

        var previous = ProfilePatch.Snapshot(user);
        var now = timeProvider.GetUtcNow();

        var changes = ProfilePatch.Apply(user, patch, now);
        if (changes.IsFailure)
            return Result<AccountProfilePatch>.FromFailure(changes);

        var profile = changes.Value!;
        var updated = user.UpdateProfile(profile.FullName, profile.Phone, profile.AvatarUrl, profile.Address, now);
        if (updated.IsFailure)
            return Result<AccountProfilePatch>.FromFailure(updated);

        if (profile.BirthDate is { } birthDate)
        {
            var birthDateChanged = user.ChangeBirthDate(birthDate, now);
            if (birthDateChanged.IsFailure)
                return Result<AccountProfilePatch>.FromFailure(birthDateChanged);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.AccountProfileUpdated, AuditTargets.Account, id.Value, ActorId: id.Value), cancellationToken)
            .ConfigureAwait(false);

        return Result<AccountProfilePatch>.Success(previous);
    }

    public async Task<bool> VerifyPasswordAsync(Guid userId, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        var user = await userRepository.GetByIdAsync(new UserId(userId), cancellationToken).ConfigureAwait(false);

        return user is not null && passwordHasher.Verify(password, user.PasswordHash.Value);
    }
}
