using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reports.Responses;

/// <summary>Monta as denúncias com as avaliações denunciadas, numa consulta só.</summary>
public sealed class ReviewReportResponseBuilder(IReviewQueries reviewQueries, ReviewResponseBuilder reviewBuilder)
{
    public async Task<IReadOnlyList<ReviewReportResponse>> BuildAsync(
        IReadOnlyList<ReviewReportRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
            return [];

        var reviewRows = await reviewQueries
            .GetManyAsync([.. rows.Select(r => r.ReviewId.Value).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var reviews = (await reviewBuilder.BuildAsync([.. reviewRows.Values], ReviewView.Admin, cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => r.Id);

        return [.. rows.Select(row => new ReviewReportResponse(
            row.Id.Value,
            row.ReviewId.Value,
            ReviewPartyValues.ToWire(row.ReporterSide),
            ReviewReportReasonValues.ToWire(row.Reason),
            row.Description,
            ReviewReportStatusValues.ToWire(row.Status),
            row.CreatedAt,
            row.ResolvedAt,
            row.ResolutionNote,
            reviews.GetValueOrDefault(row.ReviewId.Value)))];
    }

    public async Task<ReviewReportResponse> BuildAsync(ReviewReportRow row, CancellationToken cancellationToken)
    {
        var items = await BuildAsync([row], cancellationToken).ConfigureAwait(false);
        return items[0];
    }

    /// <summary>A linha de uma denúncia que o handler acabou de gravar.</summary>
    public static ReviewReportRow ToRow(ReviewReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new ReviewReportRow(
            report.Id,
            report.ReviewId,
            report.ReportedBy,
            report.ReporterSide,
            report.Reason,
            report.Description,
            report.Status,
            report.CreatedAt,
            report.ResolvedAt,
            report.ResolvedBy,
            report.ResolutionNote);
    }
}
