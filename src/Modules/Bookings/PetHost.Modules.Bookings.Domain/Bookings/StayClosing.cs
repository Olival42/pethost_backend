namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>O que a rotina da plataforma fez com uma estadia que já devia ter acabado (<see cref="Booking.CloseOverdueStay"/>).</summary>
public enum StayClosing
{
    /// <summary>Nada: antes do dia de saída, outro status ou o lembrete já foi.</summary>
    None,

    /// <summary>Reserva paga em que o pet não chegou: cancelada pela plataforma, com 50% de reembolso.</summary>
    NoShow,

    /// <summary>Estadia em andamento no dia de saída: lembrar o anfitrião de fazer o check-out.</summary>
    CheckOutReminder,

    /// <summary>Estadia em andamento 3 dias depois do dia de saída: a plataforma fez o check-out.</summary>
    AutoCheckedOut,
}
