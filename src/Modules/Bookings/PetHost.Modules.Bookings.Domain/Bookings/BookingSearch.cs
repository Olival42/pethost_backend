namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>
/// Filtro da listagem de reservas: as de um tutor ou as de um anfitrião (as listas "minhas"); o
/// admin combina os filtros que quiser, ou nenhum.
/// </summary>
/// <param name="OwnerId">Reservas pedidas por este tutor.</param>
/// <param name="HostId">Reservas dos cantinhos deste anfitrião.</param>
/// <param name="Statuses">Só estes status; vazio = todos.</param>
/// <param name="Page">Começa em 1.</param>
/// <param name="ListingId">Reservas deste cantinho.</param>
public sealed record BookingSearch(
    Guid? OwnerId,
    Guid? HostId,
    IReadOnlyCollection<BookingStatus> Statuses,
    int Page,
    int PageSize,
    Guid? ListingId = null)
{
    public static BookingSearch ForOwner(Guid ownerId, IReadOnlyCollection<BookingStatus> statuses, int page, int pageSize) =>
        new(ownerId, null, statuses, page, pageSize);

    public static BookingSearch ForHost(Guid hostId, IReadOnlyCollection<BookingStatus> statuses, int page, int pageSize) =>
        new(null, hostId, statuses, page, pageSize);
}
