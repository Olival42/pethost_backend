using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Contracts.Pets;

namespace PetHost.Modules.Bookings.Infrastructure.Adapters;

/// <summary>Adaptador da porta <see cref="IBookingPets"/> sobre o contrato público <see cref="IPetDirectory"/>.</summary>
internal sealed class BookingPets(IPetDirectory petDirectory) : IBookingPets
{
    public async Task<IReadOnlyList<BookablePet>> GetAsync(IReadOnlyCollection<Guid> petIds, CancellationToken cancellationToken)
    {
        var pets = await petDirectory.GetByIdsAsync(petIds, cancellationToken).ConfigureAwait(false);

        return [.. pets.Select(p => new BookablePet(p.Id, p.OwnerId, p.Name, p.Species, p.Size, p.IsActive))];
    }
}
