namespace PetHost.Modules.Reviews.Presentation.Reports;

/// <summary>Query string de <c>GET /api/v1/reviews/reports</c>.</summary>
public sealed class ReviewReportsRequest
{
    /// <summary><c>pending</c> (padrão), <c>upheld</c>, <c>dismissed</c> ou <c>all</c>.</summary>
    public string Status { get; init; } = "pending";

    /// <summary>Começa em 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>De 1 a 100.</summary>
    public int PageSize { get; init; } = 20;
}
