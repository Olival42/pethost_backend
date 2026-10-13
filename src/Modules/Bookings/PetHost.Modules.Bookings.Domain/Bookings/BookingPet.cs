namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>
/// Um pet da reserva (tabela <c>booking.booking_pets</c>). Guarda só o id do pet
/// (<c>pet.pets.id</c>, FK lógica): a ficha continua no módulo Pets.
/// </summary>
public sealed class BookingPet
{
    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private BookingPet()
    {
    }

    internal BookingPet(Guid petId) => PetId = petId;

    public Guid PetId { get; private set; }
}
