using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.ListOwnerReviews;

/// <summary>
/// O que os anfitriões escreveram de um tutor (e dos pets dele). Para o anfitrião decidir sobre um
/// pedido; a rota só aceita anfitrião e admin.
/// </summary>
/// <param name="OwnerId">O tutor (<c>owner.owners.id</c>), como vem na reserva.</param>
public sealed record ListOwnerReviewsQuery(Guid OwnerId, int Page = 1, int PageSize = 20) : IQuery<PagedResult<ReviewResponse>>;
