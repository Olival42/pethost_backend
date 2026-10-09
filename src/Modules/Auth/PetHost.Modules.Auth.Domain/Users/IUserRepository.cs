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

    Task<bool> ExistsByEmailAndRoleAsync(Email email, UserRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Todas as contas com esse e-mail — no máximo uma por papel. É a base do
    /// seletor de contas e da troca entre tutor e anfitrião.
    /// </summary>
    Task<IReadOnlyList<User>> ListByEmailAsync(Email email, CancellationToken cancellationToken);

    void Add(User user);

    /// <summary>Só para compensação: desfazer uma conta recém-criada num cadastro que falhou.</summary>
    void Remove(User user);
}
