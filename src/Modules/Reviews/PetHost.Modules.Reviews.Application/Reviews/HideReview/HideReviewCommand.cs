using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.HideReview;

/// <summary>O admin oculta uma avaliação direto, sem esperar denúncia (ou de novo, depois de desocultar).</summary>
/// <param name="AdminUserId">Vem do access token.</param>
/// <param name="ReviewId">Vem da rota.</param>
/// <param name="Reason">Obrigatório, até 1.000 caracteres. O autor vê.</param>
public sealed record HideReviewCommand(Guid AdminUserId, Guid ReviewId, string? Reason) : ICommand<ReviewResponse>;
