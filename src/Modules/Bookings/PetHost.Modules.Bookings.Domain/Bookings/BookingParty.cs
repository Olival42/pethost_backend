namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>Um dos dois lados da reserva: quem pediu (tutor) ou quem recebe (anfitrião).</summary>
public enum BookingParty
{
    /// <summary>O tutor que pediu a estadia.</summary>
    Owner,

    /// <summary>O anfitrião dono do cantinho.</summary>
    Host,
}
