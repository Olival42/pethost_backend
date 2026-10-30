using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IUserRepository"/> sobre o EF Core.</summary>
internal sealed class UserRepository(AuthDbContext dbContext) : IUserRepository
{
    /// <summary>
    /// Rastreado de propósito: o usuário devolvido aqui pode ter a senha trocada
    /// no mesmo caso de uso.
    /// </summary>
    public Task<User?> GetByEmailAndRoleAsync(
        Email email,
        UserRole role,
        CancellationToken cancellationToken) =>
        dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.Role == role, cancellationToken);

    public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken) =>
        dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<bool> ExistsByEmailAndRoleAsync(
        Email email,
        UserRole role,
        CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == email && u.Role == role, cancellationToken);

    public async Task<IReadOnlyList<User>> ListByEmailAsync(Email email, CancellationToken cancellationToken) =>
        await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Email == email)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(User user) => dbContext.Users.Add(user);

    public void Remove(User user) => dbContext.Users.Remove(user);
}
