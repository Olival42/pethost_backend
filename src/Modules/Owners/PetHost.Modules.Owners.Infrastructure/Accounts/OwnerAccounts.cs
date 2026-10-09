using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Infrastructure.Accounts;

/// <summary>
/// Adaptador da porta <see cref="IOwnerAccounts"/> sobre os contratos públicos do Auth:
/// <see cref="IUserDirectory"/> para ler, <see cref="IAccountRegistrar"/> para criar,
/// <see cref="IAccountProfileEditor"/> para alterar e <see cref="IAccountStatusManager"/>
/// para inativar e reativar.
/// </summary>
internal sealed class OwnerAccounts(
    IUserDirectory userDirectory,
    IAccountRegistrar accountRegistrar,
    IAccountProfileEditor accountProfileEditor,
    IAccountStatusManager accountStatusManager) : IOwnerAccounts
{
    public Task<IReadOnlyList<AccountSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) =>
        userDirectory.GetByIdsAsync(userIds, cancellationToken);

    public Task<Result> ValidateAsync(AccountRegistration registration, CancellationToken cancellationToken) =>
        accountRegistrar.ValidateAsync(registration, cancellationToken);

    public Task<Result<AccountSession>> CreateAsync(AccountRegistration registration, CancellationToken cancellationToken) =>
        accountRegistrar.RegisterAsync(registration, cancellationToken);

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        accountRegistrar.DeleteAsync(userId, cancellationToken);

    public Task<Result> ValidateProfileAsync(Guid userId, AccountProfilePatch patch, CancellationToken cancellationToken) =>
        accountProfileEditor.ValidateAsync(userId, patch, cancellationToken);

    public Task<Result<AccountProfilePatch>> UpdateProfileAsync(Guid userId, AccountProfilePatch patch, CancellationToken cancellationToken) =>
        accountProfileEditor.UpdateAsync(userId, patch, cancellationToken);

    public Task<bool> VerifyPasswordAsync(Guid userId, string? password, CancellationToken cancellationToken) =>
        accountProfileEditor.VerifyPasswordAsync(userId, password, cancellationToken);

    public Task<Result> DeactivateAsync(Guid userId, CancellationToken cancellationToken) =>
        accountStatusManager.DeactivateAsync(userId, cancellationToken);

    public Task<Result<AccountSession>> ReactivateAsync(string? email, string? password, CancellationToken cancellationToken) =>
        accountStatusManager.ReactivateAsync(email, password, Roles.Owner, cancellationToken);

    public Task<Result> SuspendAsync(Guid userId, string? reason, Guid adminId, CancellationToken cancellationToken) =>
        accountStatusManager.SuspendAsync(userId, reason, adminId, cancellationToken);

    public Task<Result> LiftSuspensionAsync(Guid userId, CancellationToken cancellationToken) =>
        accountStatusManager.LiftSuspensionAsync(userId, cancellationToken);
}
