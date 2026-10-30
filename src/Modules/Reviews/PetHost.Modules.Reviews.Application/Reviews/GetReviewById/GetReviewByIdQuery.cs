using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.GetReviewById;

/// <summary>Uma avaliação, pelo id. Funciona sem login para a avaliação que o tutor fez do anfitrião.</summary>
/// <param name="UserId">Vem do access token; nulo sem login.</param>
/// <param name="Role">Papel do token; nulo sem login.</param>
public sealed record GetReviewByIdQuery(Guid ReviewId, Guid? UserId = null, string? Role = null) : IQuery<ReviewResponse>;
