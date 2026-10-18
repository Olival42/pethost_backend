using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.ListReviews;

/// <summary>As avaliações públicas, com filtros somados. Sem login.</summary>
/// <param name="HostId">Do anfitrião (empresa: todas as filiais).</param>
/// <param name="ListingId">Do cantinho (empresa: a filial).</param>
/// <param name="Page">Começa em 1.</param>
/// <param name="PageSize">De 1 a 100.</param>
public sealed record ListReviewsQuery(
    Guid? HostId = null,
    Guid? ListingId = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ReviewResponse>>;
