using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reports;

/// <summary>
/// Ocultar uma avaliação fecha junto as denúncias pendentes dela, como procedentes e com o mesmo
/// motivo: o assunto está resolvido e o admin não precisa julgar uma por uma. Vale para a denúncia
/// procedente e para o admin ocultando direto.
/// </summary>
internal static class ReviewModeration
{
    /// <summary>
    /// Oculta <paramref name="review"/> (se ainda não estava) e fecha as denúncias pendentes dela.
    /// Devolve se a avaliação mudou e as denúncias fechadas aqui (para a trilha). Quem chama grava.
    /// </summary>
    public static async Task<(bool Hidden, IReadOnlyList<ReviewReport> Upheld)> HideAsync(
        IReviewReportRepository reportRepository,
        Review review,
        Guid adminUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var hidden = review.Hide(adminUserId, reason, now);

        var pending = await reportRepository.ListPendingForReviewAsync(review.Id, cancellationToken).ConfigureAwait(false);
        var upheld = new List<ReviewReport>();

        // A denúncia que o admin acabou de julgar vem rastreada e já não está pendente: fica de fora.
        foreach (var report in pending.Where(r => r.IsPending))
        {
            if (report.Resolve(ReviewReportStatus.Upheld, adminUserId, reason, now).IsSuccess)
                upheld.Add(report);
        }

        return (hidden, upheld);
    }
}
