using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Auth.Infrastructure.Accounts;

/// <summary>
/// Adaptador do contrato de leitura <see cref="IUserDirectory"/> (§5): outros módulos
/// leem dados de conta por aqui, sem tocar no schema <c>auth</c>.
/// </summary>
internal sealed class UserDirectory(AuthDbContext dbContext) : IUserDirectory
{
    public async Task<IReadOnlyList<AccountSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
            return [];

        var ids = userIds.Select(id => new UserId(id)).ToList();

        var users = await dbContext.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. users.Select(ToSummary)];
    }

    private static AccountSummary ToSummary(User user) =>
        new(
            user.Id.Value,
            user.FullName.Value,
            user.Email.Value,
            UserRoleValues.ToWire(user.Role),
            user.Phone?.Value,
            user.AvatarUrl?.Value,
            user.BirthDate,
            AddressMapping.ToAddressData(user.Address),
            user.IsActive,
            user.CreatedAt,
            user.SuspendedAt,
            user.SuspensionReason);
}
