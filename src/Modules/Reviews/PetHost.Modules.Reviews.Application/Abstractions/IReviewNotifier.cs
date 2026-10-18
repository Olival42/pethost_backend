using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Abstractions;

/// <summary>
/// Avisos por e-mail do módulo, pela caixa de saída. A implementação é best-effort: falha vai para o
/// log e nunca derruba o request (a avaliação já foi gravada).
/// </summary>
public interface IReviewNotifier
{
    /// <summary>Avisa o avaliado de que recebeu uma avaliação (nota e quem escreveu; o texto fica no app).</summary>
    Task NotifyReceivedAsync(
        ReviewContact recipient,
        ReviewParty authorSide,
        string authorName,
        int rating,
        CancellationToken cancellationToken);

    /// <summary>Lembra um dos lados de avaliar a estadia, até <paramref name="deadline"/>.</summary>
    Task SendReminderAsync(
        ReviewContact recipient,
        ReviewParty recipientSide,
        DateTimeOffset deadline,
        CancellationToken cancellationToken);

    /// <summary>Avisa a moderação de uma denúncia nova (sem o texto da avaliação nem a descrição).</summary>
    Task AlertModeratorsAsync(ReviewReportId reportId, ReviewReportReason reason, CancellationToken cancellationToken);
}
