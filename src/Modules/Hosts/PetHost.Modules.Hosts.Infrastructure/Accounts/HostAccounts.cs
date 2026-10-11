using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Infrastructure.Accounts;

/// <summary>
/// Adaptador da porta <see cref="IHostAccounts"/> sobre os contratos públicos do Auth: ler
/// (<see cref="IUserDirectory"/>), alterar o perfil (<see cref="IAccountProfileEditor"/>) e
/// mudar o status (<see cref="IAccountStatusManager"/>).
/// </summary>
internal sealed class HostAccounts(
    IUserDirectory userDirectory,
    IAccountProfileEditor accountProfileEditor,
    IAccountStatusManager accountStatusManager) : IHostAccounts
{
    public Task<IReadOnlyList<AccountSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) =>
        userDirectory.GetByIdsAsync(userIds, cancellationToken);

    public Task<Result> ValidateProfileAsync(Guid userId, AccountProfilePatch patch, CancellationToken cancellationToken) =>
        accountProfileEditor.ValidateAsync(userId, patch, cancellationToken);

    public Task<Result<AccountProfilePatch>> UpdateProfileAsync(Guid userId, AccountProfilePatch patch, CancellationToken cancellationToken) =>
        accountProfileEditor.UpdateAsync(userId, patch, cancellationToken);

    public Task<Result> DeactivateAsync(Guid userId, CancellationToken cancellationToken) =>
        accountStatusManager.DeactivateAsync(userId, cancellationToken);

    public Task<Result<AccountSession>> ReactivateAsync(string? email, string? password, CancellationToken cancellationToken) =>
        accountStatusManager.ReactivateAsync(email, password, Roles.Host, cancellationToken);

    public Task<Result> SuspendAsync(Guid userId, string? reason, Guid adminId, CancellationToken cancellationToken) =>
        accountStatusManager.SuspendAsync(userId, reason, adminId, cancellationToken);

    public Task<Result> LiftSuspensionAsync(Guid userId, CancellationToken cancellationToken) =>
        accountStatusManager.LiftSuspensionAsync(userId, cancellationToken);
}
