namespace PetHost.Shared.Contracts.Authorization;

/// <summary>
/// Papéis como aparecem na claim <c>role</c> do JWT, para uso em
/// <c>[Authorize(Roles = ...)]</c>.
/// </summary>
/// <remarks>
/// Duplica de propósito os valores de <c>UserRoleValues</c>, do domínio de Auth:
/// a Presentation não pode referenciar Domain (§4). Um teste de arquitetura
/// garante que as duas listas não saiam de sincronia.
/// </remarks>
public static class Roles
{
    /// <summary>Tutor.</summary>
    public const string Owner = "owner";

    /// <summary>Anfitrião.</summary>
    public const string Host = "host";

    public const string Admin = "admin";
}
