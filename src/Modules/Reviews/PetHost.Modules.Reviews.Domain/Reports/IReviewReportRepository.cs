using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Domain.Reports;

/// <summary>Acesso a <see cref="ReviewReport"/>. A Application só conhece esta interface (§11).</summary>
public interface IReviewReportRepository
{
    /// <summary>Pelo id. Rastreado: quem chama pode alterar e gravar.</summary>
    Task<ReviewReport?> GetByIdAsync(ReviewReportId id, CancellationToken cancellationToken);

    /// <summary>As denúncias ainda pendentes da avaliação <paramref name="reviewId"/>. Rastreadas.</summary>
    Task<IReadOnlyList<ReviewReport>> ListPendingForReviewAsync(ReviewId reviewId, CancellationToken cancellationToken);

    void Add(ReviewReport report);
}
