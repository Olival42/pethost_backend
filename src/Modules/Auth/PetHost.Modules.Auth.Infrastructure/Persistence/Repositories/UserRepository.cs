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

    public void Add(User user) => dbContext.Users.Add(user);
}
