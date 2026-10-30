namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>
/// Um dos lados da reserva: quem pediu (tutor) ou quem recebe (anfitrião). A plataforma só
/// aparece como quem cancelou, quando uma parcela vence antes do check-in e não é paga.
/// </summary>
public enum BookingParty
{
    /// <summary>O tutor que pediu a estadia.</summary>
    Owner,

    /// <summary>O anfitrião dono do cantinho.</summary>
    Host,

    /// <summary>O PetHost (cancelamento automático por parcela não paga).</summary>
    Platform,
}
