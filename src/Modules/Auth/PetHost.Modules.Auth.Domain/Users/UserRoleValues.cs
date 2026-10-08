namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Representação textual da role: a mesma no banco e no JSON da API
/// (dicionário de dados, seção 7 — <c>owner</c>, <c>host</c>, <c>admin</c>).
/// Fonte única da verdade, usada pela configuração do EF e pela Application.
/// </summary>
public static class UserRoleValues
{
    public const string Owner = "owner";
    public const string Host = "host";
    public const string Admin = "admin";

    public static string ToWire(UserRole role) => role switch
    {
        UserRole.Owner => Owner,
        UserRole.Host => Host,
        UserRole.Admin => Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unmapped user role."),
    };

    /// <summary>Aceita <c>owner</c>/<c>OWNER</c>/<c>Owner</c>. Recusa qualquer outro valor.</summary>
    public static bool TryParse(string? value, out UserRole role)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case Owner:
                role = UserRole.Owner;
                return true;
            case Host:
                role = UserRole.Host;
                return true;
            case Admin:
                role = UserRole.Admin;
                return true;
            default:
                role = default;
                return false;
        }
    }
}
