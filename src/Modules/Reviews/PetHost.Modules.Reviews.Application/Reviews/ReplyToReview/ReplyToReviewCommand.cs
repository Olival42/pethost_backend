using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.ReplyToReview;

/// <summary>O avaliado responde em público à avaliação (ou troca a resposta).</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="ReviewId">Vem da rota.</param>
/// <param name="Text">Até 1.000 caracteres. Obrigatório.</param>
public sealed record ReplyToReviewCommand(Guid UserId, string Role, Guid ReviewId, string? Text) : ICommand<ReviewResponse>;
