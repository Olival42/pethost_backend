using FluentValidation;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.DeclineBooking;

/// <summary>O anfitrião recusa o pedido, com um motivo opcional que o tutor vê.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="BookingId">Vem da rota.</param>
/// <param name="Reason">Opcional, até 500 caracteres.</param>
public sealed record DeclineBookingCommand(Guid UserId, Guid BookingId, string? Reason) : ICommand<BookingResponse>;

public sealed class DeclineBookingCommandValidator : AbstractValidator<DeclineBookingCommand>
{
    public DeclineBookingCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage("Booking id is required.");

        RuleFor(x => x.Reason)
            .MaximumLength(Booking.ReasonMaxLength).WithMessage(BookingsErrors.ReasonTooLong.Message);
    }
}

/// <summary>
/// Só o anfitrião do cantinho, ativo, e só pedido ainda sem resposta. Prazo vencido: a reserva
/// expira e a recusa volta <c>422 BOOKING_RESPONSE_EXPIRED</c>. Avisa o tutor por e-mail.
/// </summary>
public sealed class DeclineBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingParties bookingParties,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<DeclineBookingCommand, BookingResponse>
{
    public async Task<Result<BookingResponse>> HandleAsync(DeclineBookingCommand command, CancellationToken cancellationToken)
    {
        var loaded = await BookingAccess
            .LoadForHostAsync(bookingRepository, bookingParties, command.UserId, command.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<BookingResponse>.FromFailure(loaded);

        var booking = loaded.Value!;
        var now = timeProvider.GetUtcNow();

        if (booking.Expire(now))
        {
            var expired = await SaveAndRecordAsync(booking, AuditActions.BookingExpired, cancellationToken).ConfigureAwait(false);
            return Result<BookingResponse>.FromFailure(expired.IsFailure ? expired : Result.Failure(BookingsErrors.ResponseExpired));
        }

        var declined = booking.Decline(command.Reason, now);
        if (declined.IsFailure)
            return Result<BookingResponse>.FromFailure(declined);

        var saved = await SaveAndRecordAsync(booking, AuditActions.BookingDeclined, cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<BookingResponse>.FromFailure(saved);

        var response = await responseBuilder.BuildAsync(booking, BookingParty.Host, cancellationToken).ConfigureAwait(false);
        await notifications.DeclinedAsync(booking, response, cancellationToken).ConfigureAwait(false);

        return Result<BookingResponse>.Success(response);
    }

    private async Task<Result> SaveAndRecordAsync(Booking booking, string action, CancellationToken cancellationToken)
    {
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsSuccess)
            await BookingTrail.RecordAsync(auditTrail, action, booking, cancellationToken).ConfigureAwait(false);

        return saved;
    }
}
