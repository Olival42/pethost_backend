using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence;

/// <summary>Confirma a transação do módulo Auth.</summary>
internal sealed class AuthUnitOfWork(AuthDbContext dbContext) : IAuthUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
