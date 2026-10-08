namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Acesso a <see cref="User"/>. A Application só conhece esta interface —
/// o DbContext nunca sai da Infrastructure (§11).
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Busca pelo par (e-mail, role), que é a chave de login: o mesmo e-mail pode
    /// existir uma vez como owner e uma como host.
    /// </summary>
    Task<User?> GetByEmailAndRoleAsync(Email email, UserRole role, CancellationToken cancellationToken);

    Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken);

    void Add(User user);
}
