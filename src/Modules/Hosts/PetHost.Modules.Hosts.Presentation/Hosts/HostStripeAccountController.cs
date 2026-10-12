using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Hosts.Application.Hosts.CreateMyStripeOnboardingLink;
using PetHost.Modules.Hosts.Application.Hosts.GetMyStripeAccount;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Application.Hosts.StartMyStripeOnboarding;
using PetHost.Modules.Hosts.Application.Hosts.SyncMyStripeAccount;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Presentation.Hosts;

/// <summary>
/// "Meus recebimentos": o cadastro de recebimento do anfitrião no Stripe Connect. Só existe depois
/// que o admin aprova a verificação (antes disso, <c>404 HOST_NOT_FOUND</c>); os cantinhos só
/// aparecem na busca depois que o Stripe libera os repasses.
/// </summary>
[ApiController]
[Route("api/v1/hosts/me/stripe-account")]
[Authorize(Roles = Roles.Host)]
public sealed class HostStripeAccountController(
    IQueryHandler<GetMyStripeAccountQuery, StripeAccountResponse> getMyStripeAccount,
    ICommandHandler<StartMyStripeOnboardingCommand, StripeOnboardingSessionResponse> startMyOnboarding,
    ICommandHandler<CreateMyStripeOnboardingLinkCommand, StripeOnboardingLinkResponse> createMyOnboardingLink,
    ICommandHandler<SyncMyStripeAccountCommand, StripeAccountResponse> syncMyStripeAccount) : ControllerBase
{
    /// <summary>Situação do cadastro de recebimento, como está gravada (não chama o Stripe).</summary>
    /// <remarks>
    /// <c>status</c>: <c>not_started</c>, <c>in_progress</c>, <c>under_review</c>, <c>restricted</c>
    /// ou <c>enabled</c>. <c>listingsVisible</c> diz se os cantinhos já aparecem na busca.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMineAsync(CancellationToken cancellationToken)
    {
        var result = await getMyStripeAccount.HandleAsync(new GetMyStripeAccountQuery(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Abre ou continua o cadastro com o componente <b>embutido</b> do Stripe.</summary>
    /// <remarks>
    /// Na primeira vez cria a conta conectada, já preenchida com os dados conferidos na verificação.
    /// Devolve <c>clientSecret</c> e <c>publishableKey</c> para o app iniciar o componente
    /// <c>account-onboarding</c> (Connect.js). A sessão é de uso único e vence em minutos
    /// (<c>expiresAt</c>, Unix time): peça outra a cada abertura. Inativo ou suspenso:
    /// 422 <c>HOST_STRIPE_ONBOARDING_NOT_ALLOWED</c>; Stripe fora do ar ou não configurado:
    /// 503 <c>HOST_STRIPE_UNAVAILABLE</c>; dado recusado pelo Stripe: 422 <c>HOST_STRIPE_REQUEST_REJECTED</c>.
    /// </remarks>
    [HttpPost("onboarding-session")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [ProducesResponseType<ApiResponse<StripeOnboardingSessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<StripeOnboardingSessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<StripeOnboardingSessionResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<StripeOnboardingSessionResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<StripeOnboardingSessionResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<StripeOnboardingSessionResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<StripeOnboardingSessionResponse>>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiResponse<StripeOnboardingSessionResponse>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StartOnboardingAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var result = await startMyOnboarding.HandleAsync(new StartMyStripeOnboardingCommand(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Abre ou continua o cadastro pela página <b>hospedada</b> do Stripe.</summary>
    /// <remarks>
    /// Mesmas regras da sessão embutida. Devolve um <c>url</c> de uso único, que vence em minutos.
    /// No fim o Stripe volta para <c>Stripe:ConnectReturnUrl</c> (link vencido:
    /// <c>Stripe:ConnectRefreshUrl</c>, onde o app pede outro). Ao voltar, o app chama
    /// <c>POST .../synchronization</c> para atualizar a situação na hora.
    /// </remarks>
    [HttpPost("onboarding-link")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [ProducesResponseType<ApiResponse<StripeOnboardingLinkResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<StripeOnboardingLinkResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<StripeOnboardingLinkResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<StripeOnboardingLinkResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<StripeOnboardingLinkResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<StripeOnboardingLinkResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<StripeOnboardingLinkResponse>>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiResponse<StripeOnboardingLinkResponse>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateOnboardingLinkAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var result = await createMyOnboardingLink.HandleAsync(new CreateMyStripeOnboardingLinkCommand(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Relê a conta no Stripe agora, sem esperar o webhook.</summary>
    /// <remarks>
    /// Para o app chamar ao voltar do cadastro. Ainda sem conta conectada:
    /// 404 <c>HOST_STRIPE_ACCOUNT_NOT_FOUND</c>. Quando o Stripe libera os repasses, os cantinhos
    /// entram na busca e o anfitrião recebe um e-mail.
    /// </remarks>
    [HttpPost("synchronization")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiResponse<StripeAccountResponse>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SyncMineAsync(CancellationToken cancellationToken)
    {
        var result = await syncMyStripeAccount.HandleAsync(new SyncMyStripeAccountCommand(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }
}
