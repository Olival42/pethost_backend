using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.RemoveReviewReply;

/// <summary>O avaliado apaga a resposta que deu.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="ReviewId">Vem da rota.</param>
public sealed record RemoveReviewReplyCommand(Guid UserId, string Role, Guid ReviewId) : ICommand<ReviewResponse>;
