namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Tipo da conta. Escolhido no cadastro e imutável depois (dicionário, <c>users.role</c>).
/// Persistido como string minúscula: <c>owner</c>, <c>host</c>, <c>admin</c>.
/// </summary>
public enum UserRole
{
    /// <summary>Tutor: dono do pet.</summary>
    Owner = 1,

    /// <summary>Anfitrião: oferece o cantinho.</summary>
    Host = 2,

    /// <summary>Administrador. Criado apenas pelo seed, nunca pela API.</summary>
    Admin = 3,
}
