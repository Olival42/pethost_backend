namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>Query string de <c>GET /api/v1/reviews</c>. Os filtros se somam (E).</summary>
public sealed class ReviewsRequest
{
    /// <summary>Do anfitrião (empresa: todas as filiais).</summary>
    public Guid? HostId { get; init; }

    /// <summary>Do cantinho (empresa: a filial).</summary>
    public Guid? ListingId { get; init; }

    /// <summary>Começa em 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>De 1 a 100.</summary>
    public int PageSize { get; init; } = 20;
}
