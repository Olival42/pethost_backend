using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>
/// Conferência de e-mail + senha + papel, compartilhada por login e reativação.
/// </summary>
public static class UserCredentials
{
    /// <summary>
    /// Valor usado só para gastar tempo de CPU quando a conta não existe. Precisa
    /// ser não vazio porque o Argon2 recusa entrada vazia.
    /// </summary>
    private const string TimingEqualizerPassword = "timing-equalizer";

    /// <summary>
    /// Devolve a conta se as credenciais conferem; <c>null</c> em qualquer outro caso,
    /// sem dizer qual. Conta inexistente gasta o mesmo tempo de um hash de verdade,
    /// para que a duração da resposta não revele se o e-mail está cadastrado.
    /// </summary>
    public static async Task<User?> FindAsync(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        Email email,
        UserRole role,
        string? password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(passwordHasher);

        var user = await userRepository
            .GetByEmailAndRoleAsync(email, role, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            passwordHasher.Hash(string.IsNullOrEmpty(password) ? TimingEqualizerPassword : password);
            return null;
        }

        return passwordHasher.Verify(password ?? string.Empty, user.PasswordHash.Value)
            ? user
            : null;
    }
}
