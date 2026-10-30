using FluentValidation;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.CheckInBooking;

/// <summary>
/// O anfitrião marca "chegou": o pet está com ele. A reserva vai de <c>confirmed</c> para
/// <c>in_progress</c>.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="BookingId">Vem da rota.</param>
public sealed record CheckInBookingCommand(Guid UserId, Guid BookingId) : ICommand<BookingResponse>;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class CheckInBookingCommandValidator : AbstractValidator<CheckInBookingCommand>
{
    public CheckInBookingCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithError(BookingsErrors.BookingIdRequired);
    }
}

/// <summary>Só o anfitrião do cantinho, ativo; a regra de quando pode fica em <see cref="Booking.CheckIn"/>.</summary>
public sealed class CheckInBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingParties bookingParties,
    BookingResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CheckInBookingCommand, BookingResponse>
{
    public async Task<Result<BookingResponse>> HandleAsync(CheckInBookingCommand command, CancellationToken cancellationToken)
    {
        var loaded = await BookingAccess
            .LoadForHostAsync(bookingRepository, bookingParties, command.UserId, command.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<BookingResponse>.FromFailure(loaded);

        var booking = loaded.Value!;

        var checkedIn = booking.CheckIn(timeProvider.GetUtcNow());
        if (checkedIn.IsFailure)
            return Result<BookingResponse>.FromFailure(checkedIn);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<BookingResponse>.FromFailure(saved);

        await BookingTrail.RecordAsync(auditTrail, AuditActions.BookingCheckedIn, booking, cancellationToken).ConfigureAwait(false);

        return Result<BookingResponse>.Success(await responseBuilder.BuildAsync(booking, BookingParty.Host, cancellationToken).ConfigureAwait(false));
    }
}
