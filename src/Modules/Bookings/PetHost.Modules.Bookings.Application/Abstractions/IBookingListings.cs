using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Application.Abstractions;

/// <summary>
/// Porta para o cantinho (módulo Listings) e para a situação do anfitrião dele (Hosts). §5: a
/// Application declara a interface; a Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IBookingListings
{
    /// <summary>
    /// As condições atuais do cantinho para reservar, ou <c>null</c> se ele não existe.
    /// <see cref="BookableListing.IsBookable"/> diz se ele pode ser reservado agora.
    /// </summary>
    Task<BookableListing?> FindAsync(Guid listingId, CancellationToken cancellationToken);

    /// <summary>Título e bairro de cada cantinho, numa consulta só. Quem sumiu não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, BookingListingSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken);

    /// <summary>Endereço exato de cada cantinho — só vai para o tutor de reserva confirmada.</summary>
    Task<IReadOnlyDictionary<Guid, BookingAddress>> GetAddressesAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken);
}

/// <summary>Endereço exato do cantinho, liberado depois da confirmação (escopo, regra 7).</summary>
public sealed record BookingAddress(
    string ZipCode,
    string Street,
    string Number,
    string? Complement,
    string Neighborhood,
    string City,
    string State);

/// <summary>
/// O cantinho da reserva, para exibição: título e bairro. O endereço exato não sai aqui
/// ("Bairro, não coordenada").
/// </summary>
public sealed record BookingListingSummary(Guid Id, string Title, string Neighborhood, string City, string State);
