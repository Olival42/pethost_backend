using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Reviews.Application.Reviews.GetRatingSummary;
using PetHost.Modules.Reviews.Application.Reviews.ListOwnerReviews;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>
/// O que os anfitriões escreveram de um tutor e dos pets dele. Só anfitriões (para decidir sobre um
/// pedido; o <c>ownerId</c> vem na reserva) e o admin. O próprio tutor vê em <c>/reviews/me/received</c>.
/// </summary>
[ApiController]
[Route("api/v1/owners/{ownerId:guid}/reviews")]
[Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
public sealed class OwnerReviewsController(
    IQueryHandler<ListOwnerReviewsQuery, PagedResult<ReviewResponse>> listOwnerReviews,
    IQueryHandler<GetRatingSummaryQuery, RatingSummaryResponse> getRatingSummary) : ControllerBase
{
    /// <summary>As avaliações do tutor, da mais nova para a mais antiga, com as notas dos pets.</summary>
    /// <remarks>Sem apagadas nem ocultas. Paginado. Tutor sem avaliação: lista vazia.</remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync(Guid ownerId, [FromQuery] MyReviewsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await listOwnerReviews.HandleAsync(
            new ListOwnerReviewsQuery(ownerId, request.Page, request.PageSize),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>A nota média e a contagem do tutor.</summary>
    /// <remarks>Sem avaliação: <c>average</c> some e <c>count</c> é 0.</remarks>
    [HttpGet("summary")]
    [ProducesResponseType<ApiResponse<RatingSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<RatingSummaryResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<RatingSummaryResponse>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummaryAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var result = await getRatingSummary.HandleAsync(new GetRatingSummaryQuery(OwnerId: ownerId), cancellationToken);

        return result.ToActionResult();
    }
}
