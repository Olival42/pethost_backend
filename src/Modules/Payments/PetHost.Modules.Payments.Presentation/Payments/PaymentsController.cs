using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Payments.Application.Payments.ListMyPayouts;
using PetHost.Modules.Payments.Application.Payments.ListPaymentIssues;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Payments.Presentation.Payments;

/// <summary>Pagamentos fora de uma reserva: os ganhos do anfitrião e as pendências para o admin.</summary>
[ApiController]
[Route("api/v1/payments")]
public sealed class PaymentsController(
    IQueryHandler<ListMyPayoutsQuery, PagedResult<HostEarningsResponse>> listMyPayouts,
    IQueryHandler<ListPaymentIssuesQuery, IReadOnlyList<PaymentIssueResponse>> listPaymentIssues) : ControllerBase
{
    /// <summary>"Ganhos" do anfitrião: o repasse de cada reserva paga (repassado, guardado até o check-out, estornado).</summary>
    /// <remarks>
    /// O repasse é 90% de cada parcela paga e vai para a conta conectada do Stripe no check-out;
    /// parcela paga depois do check-out é repassada assim que é paga. Paginado: <c>page</c> a partir
    /// de 1, <c>pageSize</c> até 100.
    /// </remarks>
    [HttpGet("payouts/me")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<PagedResult<HostEarningsResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<HostEarningsResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<HostEarningsResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<HostEarningsResponse>>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PagedResult<HostEarningsResponse>>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMyPayoutsAsync([FromQuery] MyPayoutsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await listMyPayouts.HandleAsync(new ListMyPayoutsQuery(User.GetUserId(), request.Page, request.PageSize), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Pendências de pagamento para o admin resolver.</summary>
    /// <remarks>
    /// Parcela em atraso (<c>installment_overdue</c>), contestação aberta ou perdida
    /// (<c>dispute_open</c>, <c>dispute_lost</c>), reembolso que falhou (<c>refund_failed</c>), repasse
    /// ou estorno que não passou (<c>payout_failed</c>, <c>payout_reversal_failed</c>). Com o id do
    /// objeto para conferir no Dashboard do Stripe. Lista administrativa, sem paginação (§13).
    /// </remarks>
    [HttpGet("issues")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PaymentIssueResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PaymentIssueResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PaymentIssueResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListIssuesAsync(CancellationToken cancellationToken)
    {
        var result = await listPaymentIssues.HandleAsync(new ListPaymentIssuesQuery(), cancellationToken);

        return result.ToActionResult();
    }
}
