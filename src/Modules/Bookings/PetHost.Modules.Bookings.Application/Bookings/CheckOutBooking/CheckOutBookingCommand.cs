using FluentValidation;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.CheckOutBooking;

/// <summary>
/// O anfitrião marca "voltou pra casa": o pet está de novo com o tutor. A reserva vai de
/// <c>in_progress</c> para <c>completed</c> (libera repasse e avaliação).
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="BookingId">Vem da rota.</param>
public sealed record CheckOutBookingCommand(Guid UserId, Guid BookingId) : ICommand<BookingResponse>;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class CheckOutBookingCommandValidator : AbstractValidator<CheckOutBookingCommand>
{
    public CheckOutBookingCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage("Booking id is required.");
    }
}

/// <summary>
/// Só o anfitrião do cantinho, ativo; a regra fica em <see cref="Booking.CheckOut"/>. Avisa o
/// pagamento, que repassa ao anfitrião o que já foi pago.
/// </summary>
public sealed class CheckOutBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingParties bookingParties,
    IBookingPayments bookingPayments,
    BookingResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CheckOutBookingCommand, BookingResponse>
{
    public async Task<Result<BookingResponse>> HandleAsync(CheckOutBookingCommand command, CancellationToken cancellationToken)
    {
        var loaded = await BookingAccess
            .LoadForHostAsync(bookingRepository, bookingParties, command.UserId, command.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<BookingResponse>.FromFailure(loaded);

        var booking = loaded.Value!;

        var checkedOut = booking.CheckOut(timeProvider.GetUtcNow());
        if (checkedOut.IsFailure)
            return Result<BookingResponse>.FromFailure(checkedOut);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<BookingResponse>.FromFailure(saved);

        await BookingTrail.RecordAsync(auditTrail, AuditActions.BookingCheckedOut, booking, cancellationToken).ConfigureAwait(false);
        await bookingPayments.BookingChangedAsync(booking.Id.Value, cancellationToken).ConfigureAwait(false);

        return Result<BookingResponse>.Success(await responseBuilder.BuildAsync(booking, BookingParty.Host, cancellationToken).ConfigureAwait(false));
    }
}
