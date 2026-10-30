using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Reviews.Application.Reports.ReportReview;
using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Modules.Reviews.Application.Reviews.CreateReview;
using PetHost.Modules.Reviews.Application.Reviews.DeleteReview;
using PetHost.Modules.Reviews.Application.Reviews.GetRatingSummary;
using PetHost.Modules.Reviews.Application.Reviews.GetReviewById;
using PetHost.Modules.Reviews.Application.Reviews.ListMyReviews;
using PetHost.Modules.Reviews.Application.Reviews.ListReviews;
using PetHost.Modules.Reviews.Application.Reviews.RemoveReviewReply;
using PetHost.Modules.Reviews.Application.Reviews.ReplyToReview;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Application.Reviews.UpdateReview;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>
/// Avaliações das estadias: depois do check-out, cada lado avalia o outro, uma vez por reserva, em
/// até 7 dias — o tutor avalia o anfitrião e o cantinho; o anfitrião avalia o tutor e, se quiser,
/// cada pet. O avaliado responde em público; qualquer um que vê denuncia. Quem pede sai sempre do
/// token — nunca do corpo — e o lado sai do papel do token.
/// </summary>
/// <remarks>
/// <b>Às cegas:</b> ninguém além do autor lê a avaliação antes de ela ser publicada — quando o outro
/// lado também avalia, ou no fim do prazo. Publicada, não muda mais (só pode ser apagada).
/// Ler a avaliação do tutor sobre o anfitrião é público. Toda avaliação guarda o anfitrião e o
/// cantinho: pessoa física (um cantinho) filtra por <c>hostId</c>; empresa (um cantinho por filial)
/// filtra por <c>listingId</c> para a filial, ou por <c>hostId</c> para a empresa inteira. O que o
/// anfitrião escreve do tutor fica em <c>/owners/{ownerId}/reviews</c>, só para anfitriões.
/// </remarks>
[ApiController]
[Route("api/v1/reviews")]
public sealed class ReviewsController(
    ICommandHandler<CreateReviewCommand, ReviewResponse> createReview,
    ICommandHandler<UpdateReviewCommand, ReviewResponse> updateReview,
    ICommandHandler<DeleteReviewCommand, Unit> deleteReview,
    ICommandHandler<ReplyToReviewCommand, ReviewResponse> replyToReview,
    ICommandHandler<RemoveReviewReplyCommand, ReviewResponse> removeReviewReply,
    ICommandHandler<ReportReviewCommand, SubmittedReviewReportResponse> reportReview,
    IQueryHandler<ListReviewsQuery, PagedResult<ReviewResponse>> listReviews,
    IQueryHandler<GetReviewByIdQuery, ReviewResponse> getReviewById,
    IQueryHandler<ListMyReviewsQuery, PagedResult<ReviewResponse>> listMyReviews,
    IQueryHandler<GetRatingSummaryQuery, RatingSummaryResponse> getRatingSummary) : ControllerBase
{
    private const string Route = "/api/v1/reviews";
    private const string Parties = $"{Roles.Owner},{Roles.Host}";

    /// <summary>Um dos lados avalia uma estadia concluída.</summary>
    /// <remarks>
    /// Corpo: <c>bookingId</c>, <c>rating</c> (inteiro de 1 a 5), <c>comment</c> (opcional, até
    /// 1.000 caracteres) e, só para o anfitrião, <c>pets</c> (<c>[{ petId, rating, comment }]</c>,
    /// pets da reserva). O tutor avalia o anfitrião; o anfitrião avalia o tutor. Reserva inexistente
    /// ou de outra pessoa: 404 <c>REVIEW_BOOKING_NOT_FOUND</c>. Antes do check-out: 422
    /// <c>REVIEW_BOOKING_NOT_COMPLETED</c>. Mais de 7 dias depois: 422 <c>REVIEW_WINDOW_CLOSED</c>.
    /// Já avaliou (mesmo que tenha apagado): 409 <c>REVIEW_ALREADY_EXISTS</c>. Perfil inativo ou
    /// suspenso: 403 <c>REVIEW_*_DEACTIVATED</c>. Às cegas: fica guardada (<c>publishedAt</c> no fim
    /// do prazo) até o outro lado avaliar; aí as duas são publicadas e cada avaliado recebe um e-mail.
    /// Limite próprio (<c>review-writes</c>, junto com editar, apagar e responder): 429
    /// <c>TOO_MANY_REQUESTS</c>.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = Parties)]
    [EnableRateLimiting(RateLimitPolicies.ReviewWrites)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await createReview.HandleAsync(
            new CreateReviewCommand(User.GetUserId(), CurrentRole(), request.BookingId, request.Rating, request.Comment, request.Pets),
            cancellationToken);

        return result.ToCreatedResult($"{Route}/{result.Value?.Id}");
    }

    /// <summary>As avaliações que os tutores fizeram, públicas, com filtro por anfitrião e/ou cantinho.</summary>
    /// <remarks>
    /// Da mais nova para a mais antiga; apagadas e ocultas pela moderação não aparecem. Filtros:
    /// <c>hostId</c>, <c>listingId</c> (somados). Paginado: <c>page</c> a partir de 1,
    /// <c>pageSize</c> até 100. O autor aparece só pelo primeiro nome e pela foto. Não precisa de login.
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync([FromQuery] ReviewsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await listReviews.HandleAsync(
            new ListReviewsQuery(request.HostId, request.ListingId, request.Page, request.PageSize),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>A nota média e a contagem de um anfitrião ou de um cantinho, públicas.</summary>
    /// <remarks>
    /// Exatamente um de <c>hostId</c> e <c>listingId</c> (senão, 400). Só as avaliações visíveis dos
    /// tutores. Sem avaliação: <c>average</c> some e <c>count</c> é 0. Não precisa de login.
    /// </remarks>
    [HttpGet("summary")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<RatingSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<RatingSummaryResponse>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummaryAsync([FromQuery] RatingSummaryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await getRatingSummary.HandleAsync(
            new GetRatingSummaryQuery(request.HostId, request.ListingId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>As avaliações que a conta logada escreveu.</summary>
    /// <remarks>
    /// Do tutor ou do anfitrião, pelo papel do token. Da mais nova para a mais antiga, cada uma com a
    /// reserva (<c>bookingId</c>), até quando dá para editar (<c>editableUntil</c>) e, se o admin
    /// ocultou, <c>moderation</c>. Paginado.
    /// </remarks>
    [HttpGet("me")]
    [Authorize(Roles = Parties)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ListWrittenAsync([FromQuery] MyReviewsRequest request, CancellationToken cancellationToken) =>
        ListMineAsync(request, received: false, cancellationToken);

    /// <summary>As avaliações que a conta logada recebeu.</summary>
    /// <remarks>
    /// O anfitrião vê o que os tutores escreveram dele; o tutor, o que os anfitriões escreveram dele
    /// (e dos pets). Sem as ocultas. Daqui o avaliado responde (<c>PUT /reviews/{id}/reply</c>).
    /// </remarks>
    [HttpGet("me/received")]
    [Authorize(Roles = Parties)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewResponse>>>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ListReceivedAsync([FromQuery] MyReviewsRequest request, CancellationToken cancellationToken) =>
        ListMineAsync(request, received: true, cancellationToken);

    /// <summary>Uma avaliação.</summary>
    /// <remarks>
    /// A do tutor sobre o anfitrião é pública (não precisa de login). A do anfitrião sobre o tutor,
    /// só anfitriões e o tutor avaliado. O autor vê a sua mesmo oculta; o admin vê tudo. Fora isso,
    /// apagada ou oculta: 404 <c>REVIEW_NOT_FOUND</c>.
    /// </remarks>
    [HttpGet("{reviewId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        var signedIn = User.Identity?.IsAuthenticated == true;

        var result = await getReviewById.HandleAsync(
            new GetReviewByIdQuery(reviewId, User.FindUserId(), signedIn ? CurrentRole() : null),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O autor edita a nota, o comentário e/ou as notas dos pets.</summary>
    /// <remarks>
    /// PATCH: só muda o que vier; <c>comment: ""</c> apaga o comentário; <c>pets</c> substitui todas.
    /// Só antes da publicação (422 <c>REVIEW_ALREADY_PUBLISHED</c>) e até 7 dias depois do check-out
    /// (422 <c>REVIEW_WINDOW_CLOSED</c>). Oculta pela moderação: 422
    /// <c>REVIEW_HIDDEN_BY_MODERATION</c>. Avaliação de outra pessoa, apagada ou inexistente: 404
    /// <c>REVIEW_NOT_FOUND</c>.
    /// </remarks>
    [HttpPatch("{reviewId:guid}")]
    [Authorize(Roles = Parties)]
    [EnableRateLimiting(RateLimitPolicies.ReviewWrites)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UpdateAsync(
        Guid reviewId,
        [FromBody] UpdateReviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await updateReview.HandleAsync(
            new UpdateReviewCommand(User.GetUserId(), CurrentRole(), reviewId, request.Rating, request.Comment, request.Pets),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O autor apaga a avaliação.</summary>
    /// <remarks>
    /// A qualquer momento, mesmo depois do prazo ou oculta. Some das listas e da média, mas a reserva
    /// não pode ser avaliada de novo por esse lado. Avaliação de outra pessoa, já apagada ou
    /// inexistente: 404 <c>REVIEW_NOT_FOUND</c>.
    /// </remarks>
    [HttpDelete("{reviewId:guid}")]
    [Authorize(Roles = Parties)]
    [EnableRateLimiting(RateLimitPolicies.ReviewWrites)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        var result = await deleteReview.HandleAsync(new DeleteReviewCommand(User.GetUserId(), CurrentRole(), reviewId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O avaliado responde em público (ou troca a resposta).</summary>
    /// <remarks>
    /// Corpo: <c>{ "text": "..." }</c>, até 1.000 caracteres. Uma resposta por avaliação, sem prazo.
    /// Só o avaliado (o anfitrião, na avaliação do tutor; o tutor, na do anfitrião); para os outros,
    /// 404 <c>REVIEW_NOT_FOUND</c>. Oculta: 422 <c>REVIEW_HIDDEN_BY_MODERATION</c>.
    /// </remarks>
    [HttpPut("{reviewId:guid}/reply")]
    [Authorize(Roles = Parties)]
    [EnableRateLimiting(RateLimitPolicies.ReviewWrites)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReplyAsync(Guid reviewId, [FromBody] ReplyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await replyToReview.HandleAsync(
            new ReplyToReviewCommand(User.GetUserId(), CurrentRole(), reviewId, request.Text),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O avaliado apaga a resposta.</summary>
    /// <remarks>Idempotente. Só o avaliado; para os outros, 404 <c>REVIEW_NOT_FOUND</c>.</remarks>
    [HttpDelete("{reviewId:guid}/reply")]
    [Authorize(Roles = Parties)]
    [EnableRateLimiting(RateLimitPolicies.ReviewWrites)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RemoveReplyAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        var result = await removeReviewReply.HandleAsync(
            new RemoveReviewReplyCommand(User.GetUserId(), CurrentRole(), reviewId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Denuncia a avaliação para a moderação.</summary>
    /// <remarks>
    /// Corpo: <c>reason</c> (<c>offensive</c>, <c>false_information</c>, <c>personal_data</c>,
    /// <c>spam</c>, <c>other</c>) e <c>description</c> (obrigatória em <c>other</c>). Quem vê a
    /// avaliação denuncia, menos o autor (422 <c>REVIEW_REPORT_OWN_REVIEW</c>). Uma por conta: 409
    /// <c>REVIEW_REPORT_ALREADY_EXISTS</c>. O admin analisa em <c>/reviews/reports</c>. Limite próprio
    /// (<c>reports</c>, cada denúncia avisa o admin por e-mail): 429 <c>TOO_MANY_REQUESTS</c>.
    /// </remarks>
    [HttpPost("{reviewId:guid}/report")]
    [Authorize(Roles = Parties)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    [ProducesResponseType<ApiResponse<SubmittedReviewReportResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<SubmittedReviewReportResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<SubmittedReviewReportResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<SubmittedReviewReportResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<SubmittedReviewReportResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<SubmittedReviewReportResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<SubmittedReviewReportResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<SubmittedReviewReportResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReportAsync(Guid reviewId, [FromBody] ReportReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await reportReview.HandleAsync(
            new ReportReviewCommand(User.GetUserId(), CurrentRole(), reviewId, request.Reason, request.Description),
            cancellationToken);

        return result.ToCreatedResult($"{Route}/reports/{result.Value?.Id}");
    }

    private async Task<IActionResult> ListMineAsync(MyReviewsRequest request, bool received, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await listMyReviews.HandleAsync(
            new ListMyReviewsQuery(User.GetUserId(), CurrentRole(), received, request.Page, request.PageSize),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Papel do token. Cada conta tem um só.</summary>
    private string CurrentRole() =>
        User.IsInRole(Roles.Admin) ? Roles.Admin
        : User.IsInRole(Roles.Host) ? Roles.Host
        : Roles.Owner;
}
