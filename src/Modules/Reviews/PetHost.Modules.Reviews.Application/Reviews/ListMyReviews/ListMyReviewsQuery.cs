using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.ListMyReviews;

/// <summary>As avaliações da conta logada: as que ela escreveu ou, com <paramref name="Received"/>, as que recebeu.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="Received"><c>true</c>: as que recebeu; <c>false</c>: as que escreveu.</param>
/// <param name="Page">Começa em 1.</param>
/// <param name="PageSize">De 1 a 100.</param>
public sealed record ListMyReviewsQuery(
    Guid UserId,
    string Role,
    bool Received = false,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ReviewResponse>>;
