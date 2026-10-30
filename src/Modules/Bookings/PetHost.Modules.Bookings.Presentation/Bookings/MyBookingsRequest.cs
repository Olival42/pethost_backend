namespace PetHost.Modules.Bookings.Presentation.Bookings;

/// <summary>Query string de <c>GET /api/v1/bookings/me</c>.</summary>
public sealed class MyBookingsRequest
{
    /// <summary>Só estes status, repetidos (<c>status=requested&amp;status=confirmed</c>) ou separados por vírgula.</summary>
    public IReadOnlyList<string>? Status { get; init; }

    /// <summary>Começa em 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>De 1 a 100.</summary>
    public int PageSize { get; init; } = 20;
}
