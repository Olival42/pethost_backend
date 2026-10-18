using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.UpdateReview;

/// <summary>
/// O autor altera a avaliação (PATCH): só muda o que vier. <c>null</c> mantém o valor atual;
/// comentário vazio (<c>""</c>) apaga o comentário; <c>pets</c> (só o anfitrião) substitui a lista.
/// </summary>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="ReviewId">Vem da rota.</param>
public sealed record UpdateReviewCommand(
    Guid UserId,
    string Role,
    Guid ReviewId,
    int? Rating,
    string? Comment,
    IReadOnlyList<ReviewPetInput>? Pets = null) : ICommand<ReviewResponse>;
