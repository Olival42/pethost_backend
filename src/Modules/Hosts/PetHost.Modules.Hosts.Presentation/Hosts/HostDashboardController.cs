using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Hosts.Application.Hosts.GetMyDashboard;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Presentation.Hosts;

/// <summary>
/// Painel do anfitrião: pedidos novos, estadias e quanto recebeu no mês, numa chamada só. Só
/// existe depois que o admin aprova a verificação (antes disso, <c>404 HOST_NOT_FOUND</c>).
/// </summary>
[ApiController]
[Route("api/v1/hosts/me/dashboard")]
[Authorize(Roles = Roles.Host)]
public sealed class HostDashboardController(
    IQueryHandler<GetMyDashboardQuery, HostDashboardResponse> getMyDashboard) : ControllerBase
{
    /// <summary>O painel do anfitrião logado.</summary>
    /// <remarks>
    /// <c>pendingRequests</c> (pedidos no prazo, do que vence primeiro), <c>awaitingPayment</c> (só o
    /// total), <c>inProgress</c>, <c>overdueCheckIns</c> (pagas com a chegada já passada e sem check-in)
    /// e <c>upcomingStays</c> (chegada hoje ou depois) trazem o total (<c>count</c>) e até 5 itens; as
    /// listas completas ficam em <c>GET /bookings/me?status=...</c>. <c>earnings</c> conta pela data
    /// do repasse, no horário de Brasília. <c>?month=yyyy-MM</c> troca só o mês dos ganhos
    /// (<c>earnings.month</c>; padrão: o atual; formato errado ou mês no futuro: 400 em <c>month</c>) —
    /// as reservas e o <c>heldAmount</c> são sempre os de agora.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<HostDashboardResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostDashboardResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostDashboardResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostDashboardResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostDashboardResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMineAsync([FromQuery] string? month, CancellationToken cancellationToken)
    {
        var result = await getMyDashboard.HandleAsync(new GetMyDashboardQuery(User.GetUserId(), month), cancellationToken);

        return result.ToActionResult();
    }
}
