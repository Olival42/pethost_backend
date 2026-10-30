using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetHost.Shared.Contracts.Responses;

namespace PetHost.Api.Support;

/// <summary>Contato do suporte, público: quem precisa dele (CPF já usado) ainda não tem conta.</summary>
/// <param name="Email">E-mail do suporte. Omitido quando não está configurado.</param>
public sealed record SupportContactResponse(string? Email);

/// <summary>
/// Como falar com o suporte. Vários erros da API mandam "falar com o suporte" — o principal é o CPF já
/// usado por outra conta (<c>409 OWNER_CPF_ALREADY_REGISTERED</c> / <c>HOST_CPF_ALREADY_REGISTERED</c>):
/// o app chama esta rota e mostra o e-mail. O endereço vem da configuração (<c>Support:Email</c>), então
/// trocar não exige nova versão do app. O admin resolve com as rotas que já existem (suspender a conta
/// indevida e liberar o CPF).
/// </summary>
[ApiController]
[Route("api/v1/support")]
[AllowAnonymous]
public sealed class SupportController(IOptions<SupportOptions> options) : ControllerBase
{
    /// <summary>O e-mail do suporte, para o app mostrar quando a API manda falar com o suporte.</summary>
    /// <remarks>Pública (quem precisa ainda não tem conta). Sem e-mail configurado, <c>email</c> vem omitido.</remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<SupportContactResponse>>(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var email = options.Value.Email?.Trim();

        return Ok(ApiResponse<SupportContactResponse>.Ok(new SupportContactResponse(string.IsNullOrEmpty(email) ? null : email)));
    }
}
