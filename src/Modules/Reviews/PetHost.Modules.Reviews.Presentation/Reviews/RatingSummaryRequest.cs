namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>Query string de <c>GET /api/v1/reviews/summary</c>: exatamente um dos dois.</summary>
public sealed class RatingSummaryRequest
{
    /// <summary>Nota do anfitrião (empresa: todas as filiais).</summary>
    public Guid? HostId { get; init; }

    /// <summary>Nota do cantinho (empresa: a filial).</summary>
    public Guid? ListingId { get; init; }
}
