namespace PetHost.Modules.Bookings.Presentation.Bookings;

/// <summary>Query string de <c>GET /api/v1/bookings</c> (admin). Os filtros se somam.</summary>
public sealed class BookingsAdminRequest
{
    /// <summary>Só estes status, repetidos (<c>status=requested&amp;status=confirmed</c>) ou separados por vírgula.</summary>
    public IReadOnlyList<string>? Status { get; init; }

    /// <summary>Só as pedidas por este tutor (id do tutor, não da conta).</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>Só as dos cantinhos deste anfitrião (id do anfitrião, não da conta).</summary>
    public Guid? HostId { get; init; }

    /// <summary>Só as deste cantinho.</summary>
    public Guid? ListingId { get; init; }

    /// <summary>Começa em 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>De 1 a 100.</summary>
    public int PageSize { get; init; } = 20;
}
