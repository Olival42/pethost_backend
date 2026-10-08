namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>Confirma a transação do módulo Auth.</summary>
public interface IAuthUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
