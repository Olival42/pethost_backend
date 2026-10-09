using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Audit.Application.Entries;
using PetHost.Modules.Audit.Application.Entries.SearchAuditEntries;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Audit.Presentation.Entries;

/// <summary>Trilha de auditoria: o que aconteceu no sistema, quem fez e de onde. Só admin.</summary>
[ApiController]
[Route("api/v1/audit-entries")]
public sealed class AuditEntriesController(
    IQueryHandler<SearchAuditEntriesQuery, PagedResult<AuditEntryResponse>> searchAuditEntries) : ControllerBase
{
    /// <summary>Busca na trilha, do mais novo para o mais antigo, paginada.</summary>
    /// <remarks>
    /// Filtros opcionais: <c>action</c> (ex.: <c>owner.suspended</c>), <c>targetType</c>
    /// (<c>account</c>, <c>owner</c>) com <c>targetId</c>, <c>actorId</c> e período
    /// (<c>from</c>/<c>to</c>, ISO 8601). <c>page</c> começa em 1; <c>pageSize</c> de 1 a 100
    /// (padrão 20). A consulta também fica registrada na trilha.
    /// </remarks>
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<PagedResult<AuditEntryResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<AuditEntryResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<AuditEntryResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<AuditEntryResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? action = null,
        [FromQuery] string? targetType = null,
        [FromQuery] Guid? targetId = null,
        [FromQuery] Guid? actorId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await searchAuditEntries.HandleAsync(
            new SearchAuditEntriesQuery(page, pageSize, action, targetType, targetId, actorId, from, to),
            cancellationToken);

        return result.ToActionResult();
    }
}
