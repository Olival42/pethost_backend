namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>Query string de <c>GET /api/v1/reviews/me</c>.</summary>
public sealed class MyReviewsRequest
{
    /// <summary>Começa em 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>De 1 a 100.</summary>
    public int PageSize { get; init; } = 20;
}
