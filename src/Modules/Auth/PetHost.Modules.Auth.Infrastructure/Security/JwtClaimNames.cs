namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Nomes das claims, curtos como no registro da IANA. O host configura
/// <c>MapInboundClaims = false</c> para que cheguem com estes mesmos nomes
/// no <c>ClaimsPrincipal</c>, em vez das URIs longas do WS-Security.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>Id do usuário.</summary>
    public const string Subject = "sub";

    public const string Email = "email";

    /// <summary>Nome completo.</summary>
    public const string Name = "name";

    /// <summary>Papel: <c>owner</c>, <c>host</c> ou <c>admin</c>. Usado por <c>[Authorize(Roles = ...)]</c>.</summary>
    public const string Role = "role";

    /// <summary>Id único do token.</summary>
    public const string TokenId = "jti";

    /// <summary>
    /// Instante da emissão em milissegundos (Unix). O <c>iat</c> padrão é em segundos, e a
    /// revogação precisa separar o token antigo do novo emitido no mesmo segundo — por
    /// exemplo, a sessão nova que a troca de senha devolve.
    /// </summary>
    public const string IssuedAtMilliseconds = "iat_ms";
}
