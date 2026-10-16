using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Payments.Application.Payments.ChoosePaymentPlan;
using PetHost.Modules.Payments.Application.Payments.GetBookingPayment;
using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Application.Payments.StartInstallmentPayment;
using PetHost.Modules.Payments.Application.Payments.SyncBookingPayment;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Payments.Presentation.Payments;

/// <summary>
/// O pagamento de uma reserva: o tutor escolhe as parcelas (1x a 6x) e paga cada uma com Pix ou
/// cartão dentro do PetHost (checkout transparente, Payment Element do Stripe). Quem confirma é o
/// webhook do Stripe (<c>POST /api/v1/stripe/webhooks</c>), nunca o retorno do navegador.
/// </summary>
[ApiController]
[Route("api/v1/bookings/{bookingId:guid}/payment")]
public sealed class BookingPaymentController(
    IQueryHandler<GetBookingPaymentQuery, PaymentResponse> getBookingPayment,
    ICommandHandler<ChoosePaymentPlanCommand, PaymentResponse> choosePaymentPlan,
    ICommandHandler<StartInstallmentPaymentCommand, PaymentSessionResponse> startInstallmentPayment,
    ICommandHandler<SyncBookingPaymentCommand, PaymentResponse> syncBookingPayment) : ControllerBase
{
    /// <summary>O pagamento da reserva: parcelas, situação de cada uma, reembolso e repasse.</summary>
    /// <remarks>
    /// Só o tutor que pediu, o anfitrião do cantinho e o admin; os outros, 404
    /// <c>PAYMENT_BOOKING_NOT_FOUND</c>. Reserva aceita e o tutor ainda não escolheu as parcelas:
    /// <c>status = not_started</c>, com <c>paymentDeadline</c> e as opções (<c>options</c>: 1x a 6x,
    /// cada parcela de pelo menos R$ 50,00). Outra reserva sem pagamento: 404 <c>PAYMENT_NOT_FOUND</c>.
    /// O anfitrião vê o repasse (<c>payout</c>); o admin, também os ids do Stripe.
    /// </remarks>
    [HttpGet]
    [Authorize]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await getBookingPayment.HandleAsync(new GetBookingPaymentQuery(User.GetUserId(), CurrentRole(), bookingId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O tutor escolhe em quantas parcelas paga (ou troca, antes de pagar).</summary>
    /// <remarks>
    /// Corpo: <c>{ "installmentCount": 3 }</c> (1 = à vista, até 6). Só reserva <c>accepted</c> e
    /// dentro do prazo da 1ª parcela (24h depois do aceite). A 1ª parcela vence no prazo; as outras, a
    /// cada 30 dias — inclusive depois da estadia. Erros: 400 em <c>installmentCount</c>; 422
    /// <c>PAYMENT_INSTALLMENT_COUNT_NOT_ALLOWED</c> (cada parcela precisa de R$ 50,00),
    /// <c>PAYMENT_PLAN_LOCKED</c> (já pagou algo), <c>PAYMENT_WINDOW_CLOSED</c>, <c>PAYMENT_CLOSED</c>
    /// (reserva cancelada ou expirada).
    /// </remarks>
    [HttpPut]
    [Authorize(Roles = Roles.Owner)]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ChoosePlanAsync(Guid bookingId, [FromBody] ChoosePaymentPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await choosePaymentPlan.HandleAsync(
            new ChoosePaymentPlanCommand(User.GetUserId(), bookingId, request.InstallmentCount),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Abre o pagamento de uma parcela: devolve o <c>clientSecret</c> para o Payment Element (cartão e Pix).</summary>
    /// <remarks>
    /// O app chama <c>stripe.elements({ clientSecret })</c> com a <c>publishableKey</c>; no Pix, o QR
    /// code aparece na própria página e vale até <c>pixExpiresAt</c> (nunca depois do prazo da
    /// parcela). A confirmação chega pelo webhook — depois de pagar, o app pode chamar
    /// <c>POST .../payment/synchronization</c> para ver o resultado na hora. Parcelas em ordem
    /// (adiantar a próxima pode): 422 <c>PAYMENT_INSTALLMENT_OUT_OF_ORDER</c>. Já paga ou sendo
    /// confirmada: 422 <c>PAYMENT_INSTALLMENT_ALREADY_PAID</c> / <c>PAYMENT_INSTALLMENT_PROCESSING</c>.
    /// 1ª parcela fora do prazo: 422 <c>PAYMENT_WINDOW_CLOSED</c>. Stripe fora do ar: 503 <c>PAYMENT_STRIPE_UNAVAILABLE</c>.
    /// </remarks>
    [HttpPost("installments/{installmentNumber:int}/payment-session")]
    [Authorize(Roles = Roles.Owner)]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [ProducesResponseType<ApiResponse<PaymentSessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PaymentSessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PaymentSessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PaymentSessionResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PaymentSessionResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<PaymentSessionResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<PaymentSessionResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<PaymentSessionResponse>>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiResponse<PaymentSessionResponse>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StartInstallmentPaymentAsync(Guid bookingId, int installmentNumber, CancellationToken cancellationToken)
    {
        var result = await startInstallmentPayment.HandleAsync(
            new StartInstallmentPaymentCommand(User.GetUserId(), bookingId, installmentNumber),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Relê agora no Stripe as cobranças da reserva (sem esperar o webhook).</summary>
    /// <remarks>
    /// O app chama ao voltar do Payment Element; o admin, para conferir. Confirma a reserva se a 1ª
    /// parcela foi paga, aplica reembolsos e contestações e faz o que estiver pendente. Stripe fora
    /// do ar: 503 <c>PAYMENT_STRIPE_UNAVAILABLE</c>.
    /// </remarks>
    [HttpPost("synchronization")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Admin}")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiResponse<PaymentResponse>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SyncAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await syncBookingPayment.HandleAsync(new SyncBookingPaymentCommand(User.GetUserId(), CurrentRole(), bookingId), cancellationToken);

        return result.ToActionResult();
    }

    private string CurrentRole() =>
        User.IsInRole(Roles.Admin) ? Roles.Admin
        : User.IsInRole(Roles.Host) ? Roles.Host
        : Roles.Owner;
}
