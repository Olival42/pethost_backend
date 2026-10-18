using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.ListPetReviews;

/// <summary>
/// O histórico do pet ("currículo"): as notas que os anfitriões deram a ele, estadia por estadia.
/// Para o anfitrião decidir sobre um pedido; a rota só aceita anfitrião e admin.
/// </summary>
/// <param name="PetId">O pet (<c>pet.pets.id</c>), como vem na reserva.</param>
public sealed record ListPetReviewsQuery(Guid PetId, int Page = 1, int PageSize = 20) : IQuery<PagedResult<PetReviewResponse>>;
