using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.UnhideReview;

/// <summary>O admin volta a mostrar uma avaliação ocultada pela moderação (procedente dado por engano).</summary>
/// <param name="AdminUserId">Vem do access token.</param>
/// <param name="ReviewId">Vem da rota.</param>
public sealed record UnhideReviewCommand(Guid AdminUserId, Guid ReviewId) : ICommand<ReviewResponse>;
