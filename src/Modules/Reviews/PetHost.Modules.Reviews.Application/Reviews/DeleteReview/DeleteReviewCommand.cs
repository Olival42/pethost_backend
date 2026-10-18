using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Reviews.Application.Reviews.DeleteReview;

/// <summary>O autor apaga a avaliação.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="ReviewId">Vem da rota.</param>
public sealed record DeleteReviewCommand(Guid UserId, string Role, Guid ReviewId) : ICommand<Unit>;
