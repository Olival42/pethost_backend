namespace PetHost.Modules.Reviews.Domain.Reports;

/// <summary>Acesso a <see cref="ReviewReport"/>. A Application só conhece esta interface (§11).</summary>
public interface IReviewReportRepository
{
    /// <summary>Pelo id. Rastreado: quem chama pode alterar e gravar.</summary>
    Task<ReviewReport?> GetByIdAsync(ReviewReportId id, CancellationToken cancellationToken);

    void Add(ReviewReport report);
}
