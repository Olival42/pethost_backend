using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Shared.Infrastructure.Stripe;

/// <summary>
/// Destino dos webhooks do Stripe — um só para a API inteira. Quem prova a origem é a assinatura
/// (<c>Stripe-Signature</c>), não um token: por isso é anônimo e fica fora do rate limit (o
/// Stripe manda rajadas e tenta de novo com espera própria).
/// </summary>
[ApiController]
[Route("api/v1/stripe/webhooks")]
public sealed class StripeWebhooksController(StripeWebhookProcessor processor) : ControllerBase
{
    /// <summary>Recebe um evento do Stripe (conta da plataforma ou contas conectadas).</summary>
    /// <remarks>
    /// Assinatura ausente, errada ou com mais de 5 minutos: <c>400</c> no campo
    /// <c>stripeSignature</c>. Sem segredo configurado: <c>503 STRIPE_WEBHOOK_UNAVAILABLE</c>. Tipo
    /// de evento sem tratamento: <c>200</c>, ignorado. Falha ao tratar: <c>500</c>, e o Stripe
    /// tenta de novo.
    /// </remarks>
    [HttpPost]
    [AllowAnonymous]
    [DisableRateLimiting]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        // O corpo é lido cru: a assinatura vale para os bytes exatos que o Stripe mandou.
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        var result = await processor.ProcessAsync(payload, Request.Headers["Stripe-Signature"].ToString(), cancellationToken);

        return result.ToActionResult();
    }
}
