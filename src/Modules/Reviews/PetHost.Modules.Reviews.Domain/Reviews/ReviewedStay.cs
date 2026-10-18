namespace PetHost.Modules.Reviews.Domain.Reviews;

/// <summary>
/// A estadia que se quer avaliar, como o módulo Bookings a descreve: quem foi o tutor, o
/// anfitrião, o cantinho e os pets, se ela terminou e quando.
/// </summary>
/// <param name="BookingId">A reserva (<c>booking.bookings.id</c>).</param>
/// <param name="OwnerId">Tutor da reserva (<c>owner.owners.id</c>).</param>
/// <param name="HostId">Anfitrião (<c>host.hosts.id</c>).</param>
/// <param name="ListingId">Cantinho (<c>listing.listings.id</c>).</param>
/// <param name="IsCompleted">A reserva está concluída (check-out feito).</param>
/// <param name="CheckedOutAt">Quando a estadia acabou. Nulo antes do check-out.</param>
/// <param name="PetIds">Os pets da reserva (<c>pet.pets.id</c>).</param>
public sealed record ReviewedStay(
    Guid BookingId,
    Guid OwnerId,
    Guid HostId,
    Guid ListingId,
    bool IsCompleted,
    DateTimeOffset? CheckedOutAt,
    IReadOnlyList<Guid> PetIds)
{
    /// <summary>O perfil <paramref name="profileId"/> é o lado <paramref name="party"/> desta estadia?</summary>
    public bool HasParticipant(ReviewParty party, Guid profileId) =>
        party == ReviewParty.Owner ? OwnerId == profileId : HostId == profileId;
}
