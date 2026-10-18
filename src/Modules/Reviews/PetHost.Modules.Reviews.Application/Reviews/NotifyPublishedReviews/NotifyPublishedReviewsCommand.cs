using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.NotifyPublishedReviews;

/// <summary>
/// Rodada da tarefa em segundo plano: avisa por e-mail o avaliado de cada avaliação que acabou de
/// ser publicada no fim do prazo (o outro lado não avaliou). Um aviso por avaliação.
/// </summary>
/// <param name="BatchSize">Quantas avaliações cada rodada olha.</param>
public sealed record NotifyPublishedReviewsCommand(int BatchSize) : ICommand<NotifyPublishedReviewsResponse>;

/// <summary>Quantas avaliações publicadas foram avisadas (inclusive quem não pode receber e-mail).</summary>
public sealed record NotifyPublishedReviewsResponse(int Notified);
