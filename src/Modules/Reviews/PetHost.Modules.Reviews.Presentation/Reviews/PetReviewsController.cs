using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Reviews.Application.Reviews.GetRatingSummary;
using PetHost.Modules.Reviews.Application.Reviews.ListPetReviews;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>
/// O histórico do pet ("currículo"): as notas que os anfitriões deram a ele nas estadias. Só
/// anfitriões (para decidir sobre um pedido; o <c>petId</c> vem na reserva) e o admin. O tutor vê as
/// notas dos pets dele em <c>/reviews/me/received</c>.
/// </summary>
[ApiController]
[Route("api/v1/pets/{petId:guid}/reviews")]
[Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
public sealed class PetReviewsController(
    IQueryHandler<ListPetReviewsQuery, PagedResult<PetReviewResponse>> listPetReviews,
    IQueryHandler<GetRatingSummaryQuery, RatingSummaryResponse> getRatingSummary) : ControllerBase
{
    /// <summary>As notas do pet, da estadia mais recente para a mais antiga.</summary>
    /// <remarks>
    /// Cada item: a nota, o comentário, o anfitrião que deu e quando a estadia acabou. Só avaliações
    /// publicadas, sem apagadas nem ocultas. Paginado. Pet sem nota: lista vazia.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<PetReviewResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<PetReviewResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<PetReviewResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<PetReviewResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync(Guid petId, [FromQuery] MyReviewsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await listPetReviews.HandleAsync(new ListPetReviewsQuery(petId, request.Page, request.PageSize), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>A nota média e a contagem do pet.</summary>
    /// <remarks>Sem nota: <c>average</c> some e <c>count</c> é 0.</remarks>
    [HttpGet("summary")]
    [ProducesResponseType<ApiResponse<RatingSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<RatingSummaryResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<RatingSummaryResponse>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummaryAsync(Guid petId, CancellationToken cancellationToken)
    {
        var result = await getRatingSummary.HandleAsync(new GetRatingSummaryQuery(PetId: petId), cancellationToken);

        return result.ToActionResult();
    }
}
