using System.Globalization;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Bookings.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Bookings (reservas). Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class BookingsErrors
{
    /// <summary>Maior página da listagem (§13).</summary>
    public const int MaxPageSize = 100;

    // --- Validação: pedido (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error ListingIdRequired =
        Error.Validation("listingId", "Listing id is required.");

    public static readonly Error PetIdsRequired =
        Error.Validation("petIds", "Choose at least one pet.");

    public static readonly Error PetIdsTooMany =
        Error.Validation("petIds", $"A booking can have at most {Booking.MaxPets} pets.");

    public static readonly Error PetIdsInvalid =
        Error.Validation("petIds", "Each pet must be a valid id and appear only once.");

    public static readonly Error CheckInRequired =
        Error.Validation("checkIn", "Check-in date is required.");

    public static readonly Error CheckOutRequired =
        Error.Validation("checkOut", "Check-out date is required.");

    public static readonly Error CheckInInvalid =
        Error.Validation("checkIn", $"Check-in must be a date in the format {StayPeriod.DateFormat}.");

    public static readonly Error CheckOutInvalid =
        Error.Validation("checkOut", $"Check-out must be a date in the format {StayPeriod.DateFormat}.");

    public static readonly Error CheckInInThePast =
        Error.Validation("checkIn", "Check-in cannot be in the past.");

    public static readonly Error CheckInTooFarAhead =
        Error.Validation("checkIn", $"Check-in can be at most {StayPeriod.MaxDaysAhead} days ahead.");

    public static readonly Error CheckOutNotAfterCheckIn =
        Error.Validation("checkOut", "Check-out must be after check-in.");

    public static readonly Error StayTooLong =
        Error.Validation("checkOut", $"A stay can have at most {StayPeriod.MaxNights} nights.");

    public static readonly Error ReasonTooLong =
        Error.Validation("reason", $"Reason must be at most {Booking.ReasonMaxLength} characters.");

    // --- Validação: listagem ---

    public static readonly Error StatusFilterInvalid =
        Error.Validation("status", $"Status must be among: {string.Join(", ", BookingStatusValues.All)}.");

    public static readonly Error PageInvalid =
        Error.Validation("page", "Page must be 1 or greater.");

    public static readonly Error PageSizeInvalid =
        Error.Validation("pageSize", $"Page size must be between 1 and {MaxPageSize}.");

    // --- Quem pede ---

    /// <summary>
    /// 404: não existe reserva com esse id, ou ela não é de quem pede (nem tutor, nem anfitrião).
    /// As duas situações respondem igual de propósito (§8).
    /// </summary>
    public static Error NotFound(BookingId id) =>
        new("BOOKING_NOT_FOUND", $"Booking '{id.Value}' was not found.");

    /// <summary>404: a conta do token não tem perfil de tutor.</summary>
    public static readonly Error OwnerNotFound =
        new("BOOKING_OWNER_NOT_FOUND", "This account has no owner profile.");

    /// <summary>403: o tutor está inativo ou suspenso.</summary>
    public static readonly Error OwnerDeactivated =
        new("BOOKING_OWNER_DEACTIVATED", "This owner profile is deactivated or suspended.");

    /// <summary>404: a conta do token não tem perfil de anfitrião (ainda não foi verificada).</summary>
    public static readonly Error HostNotFound =
        new("BOOKING_HOST_NOT_FOUND", "This account has no verified host profile yet.");

    /// <summary>403: o anfitrião está inativo ou suspenso e não pode mexer nas reservas.</summary>
    public static readonly Error HostDeactivated =
        new("BOOKING_HOST_DEACTIVATED", "This host profile is deactivated or suspended.");

    // --- Pedido ---

    /// <summary>
    /// 404: o cantinho não existe ou não pode ser reservado agora (inativo, anfitrião inativo,
    /// suspenso ou sem repasses liberados) — o mesmo critério da busca.
    /// </summary>
    public static Error ListingNotFound(Guid listingId) =>
        new("BOOKING_LISTING_NOT_FOUND", $"Listing '{listingId}' was not found.");

    /// <summary>404: o pet não existe ou não é do tutor que pede.</summary>
    public static Error PetNotFound(Guid petId) =>
        new("BOOKING_PET_NOT_FOUND", $"Pet '{petId}' was not found.");

    /// <summary>422: o pet foi desativado pelo tutor.</summary>
    public static Error PetInactive(string petName) =>
        new("BOOKING_PET_INACTIVE", $"{petName} is deactivated. Reactivate the pet before booking.");

    /// <summary>422: o cantinho não aceita a espécie do pet.</summary>
    public static Error SpeciesNotAccepted(string petName, string species) =>
        new("BOOKING_PET_SPECIES_NOT_ACCEPTED", $"This listing does not accept {species}, so {petName} cannot stay here.");

    /// <summary>422: o cantinho não aceita o porte do pet.</summary>
    public static Error SizeNotAccepted(string petName, string size) =>
        new("BOOKING_PET_SIZE_NOT_ACCEPTED", $"This listing does not accept {size} pets, so {petName} cannot stay here.");

    /// <summary>409: o pet já está em outra reserva em aberto que divide noite com este período.</summary>
    public static Error PetPeriodConflict(string petName) =>
        new("BOOKING_PET_PERIOD_CONFLICT", $"{petName} already has another booking in this period.");

    /// <summary>422: o cantinho não recebe pets num dia do período.</summary>
    public static Error WeekdayNotAvailable(DateOnly day) =>
        new(
            "BOOKING_WEEKDAY_NOT_AVAILABLE",
            string.Create(CultureInfo.InvariantCulture, $"The host does not receive pets on {day.DayOfWeek}s ({day:yyyy-MM-dd}). Every day of the stay must be one of the listing's available weekdays."));

    /// <summary>422: em alguma noite do período o cantinho passaria da capacidade.</summary>
    public static Error CapacityExceeded(DateOnly night) =>
        new(
            "BOOKING_CAPACITY_EXCEEDED",
            string.Create(CultureInfo.InvariantCulture, $"The listing has no room for these pets on the night of {night:yyyy-MM-dd}."));

    // --- Transições ---

    /// <summary>422: só pedido ainda sem resposta (Solicitada) pode ser aceito ou recusado.</summary>
    public static readonly Error NotAwaitingResponse =
        new("BOOKING_NOT_AWAITING_RESPONSE", "Only a requested booking can be accepted or declined.");

    /// <summary>422: passou o prazo de 24h para responder (ou a data de chegada). A reserva expirou.</summary>
    public static readonly Error ResponseExpired =
        new("BOOKING_RESPONSE_EXPIRED", "The time to answer this request is over; the booking has expired.");

    /// <summary>422: a reserva já foi cancelada.</summary>
    public static readonly Error AlreadyCancelled =
        new("BOOKING_ALREADY_CANCELLED", "This booking is already cancelled.");

    /// <summary>422: recusada ou expirada — não há o que cancelar.</summary>
    public static readonly Error AlreadyClosed =
        new("BOOKING_ALREADY_CLOSED", "This booking was declined or expired; there is nothing to cancel.");

    /// <summary>422: o pet já chegou (ou já voltou). Depois do check-in não se cancela.</summary>
    public static readonly Error CannotCancelAfterCheckIn =
        new("BOOKING_CANNOT_CANCEL_AFTER_CHECK_IN", "A booking cannot be cancelled after check-in.");

    /// <summary>422: check-in só em reserva Confirmada.</summary>
    public static readonly Error NotConfirmed =
        new("BOOKING_NOT_CONFIRMED", "Only a confirmed booking can be checked in.");

    /// <summary>422: ainda não chegou o dia da chegada.</summary>
    public static Error CheckInTooEarly(DateOnly checkIn) =>
        new(
            "BOOKING_CHECK_IN_TOO_EARLY",
            string.Create(CultureInfo.InvariantCulture, $"Check-in opens on {checkIn:yyyy-MM-dd}."));

    /// <summary>422: chegou o dia da saída sem check-in: a estadia não aconteceu.</summary>
    public static readonly Error CheckInWindowClosed =
        new("BOOKING_CHECK_IN_WINDOW_CLOSED", "The stay period is over; this booking can no longer be checked in.");

    /// <summary>422: check-out só em reserva Em andamento.</summary>
    public static readonly Error NotInProgress =
        new("BOOKING_NOT_IN_PROGRESS", "Only a booking in progress (after check-in) can be checked out.");

    /// <summary>409: outra pessoa alterou a reserva ao mesmo tempo. Leia de novo e tente outra vez.</summary>
    public static readonly Error Conflict =
        new("BOOKING_CONFLICT", "This booking was changed by someone else at the same time. Reload it and try again.");
}
