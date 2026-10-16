namespace PetHost.Shared.Contracts.Bookings;

/// <summary>Resultado de <see cref="IBookingPaymentOutcomes"/>.</summary>
public enum BookingPaymentOutcome
{
    /// <summary>A reserva mudou agora.</summary>
    Done,

    /// <summary>A reserva já estava assim (aviso repetido): nada mudou.</summary>
    AlreadyDone,

    /// <summary>O status da reserva não permite a mudança (ou ela não existe).</summary>
    NotApplicable,

    /// <summary>Outra pessoa alterou a reserva ao mesmo tempo: tente de novo.</summary>
    Conflict,
}
