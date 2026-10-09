using System.Security.Claims;

namespace PetHost.Shared.Infrastructure.Http;

/// <summary>Leitura do usuário autenticado a partir do access token.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Claim <c>sub</c> do JWT: o id da conta.</summary>
    public const string SubjectClaim = "sub";

    /// <summary>
    /// Id da conta do token. Só chame em ação com <c>[Authorize]</c>: lá o token já
    /// foi validado e o <c>sub</c> sempre existe.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var value = principal.FindFirstValue(SubjectClaim);

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("The access token has no valid 'sub' claim.");
    }
}
