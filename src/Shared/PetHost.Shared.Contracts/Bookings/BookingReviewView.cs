namespace PetHost.Shared.Contracts.Bookings;

/// <summary>
/// A reserva vista pelo módulo de avaliações (<see cref="IBookingDirectory.GetForReviewAsync"/>).
/// Só o que a avaliação usa: o tutor, o anfitrião, o cantinho, os pets, o status e o check-out.
/// </summary>
/// <param name="OwnerId">Tutor da reserva (<c>owner.owners.id</c>).</param>
/// <param name="HostId">Anfitrião (<c>host.hosts.id</c>).</param>
/// <param name="ListingId">Cantinho onde o pet ficou (<c>listing.listings.id</c>).</param>
/// <param name="Status">Status da reserva, nos valores <c>snake_case</c> da API (<see cref="BookingPaymentView.Completed"/>...).</param>
/// <param name="CheckedOutAt">Quando a estadia acabou ("voltou pra casa"). Nulo antes do check-out.</param>
/// <param name="PetIds">Os pets da reserva (<c>pet.pets.id</c>): o anfitrião dá nota a cada um.</param>
public sealed record BookingReviewView(
    Guid Id,
    Guid OwnerId,
    Guid HostId,
    Guid ListingId,
    string Status,
    DateTimeOffset? CheckedOutAt,
    IReadOnlyList<Guid> PetIds);
