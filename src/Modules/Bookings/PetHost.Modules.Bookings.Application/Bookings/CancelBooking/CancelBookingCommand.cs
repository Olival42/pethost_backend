using FluentValidation;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.CancelBooking;

/// <summary>
/// O tutor ou o anfitrião cancela a reserva. Vale até o check-in; depois que o pet chegou, não.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> cancela como tutor; <c>host</c>, como anfitrião.</param>
/// <param name="BookingId">Vem da rota.</param>
/// <param name="Reason">Opcional, até 500 caracteres.</param>
public sealed record CancelBookingCommand(Guid UserId, string Role, Guid BookingId, string? Reason) : ICommand<BookingResponse>;

public sealed class CancelBookingCommandValidator : AbstractValidator<CancelBookingCommand>
{
    public CancelBookingCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage("Booking id is required.");

        RuleFor(x => x.Reason)
            .MaximumLength(Booking.ReasonMaxLength).WithMessage(BookingsErrors.ReasonTooLong.Message);
    }
}

/// <summary>
/// Só o tutor que pediu ou o anfitrião do cantinho. O reembolso (<c>refundPercentage</c>) sai da
/// regra do domínio (<see cref="Booking.Cancel"/>): 100% se o anfitrião cancela ou se o tutor
/// cancela com 48h de antecedência; 50% depois disso; nenhum se nada foi pago. Avisa os dois
/// lados por e-mail.
/// </summary>
public sealed class CancelBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingParties bookingParties,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CancelBookingCommand, BookingResponse>
{
    public async Task<Result<BookingResponse>> HandleAsync(CancelBookingCommand command, CancellationToken cancellationToken)
    {
        var loaded = await BookingAccess
            .LoadForPartyAsync(bookingRepository, bookingParties, command.UserId, command.Role, command.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<BookingResponse>.FromFailure(loaded);

        var (booking, side) = loaded.Value!;

        var cancelled = booking.Cancel(side, command.Reason, timeProvider.GetUtcNow());
        if (cancelled.IsFailure)
            return Result<BookingResponse>.FromFailure(cancelled);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<BookingResponse>.FromFailure(saved);

        await BookingTrail.RecordCancelledAsync(auditTrail, booking, cancellationToken).ConfigureAwait(false);

        var response = await responseBuilder.BuildAsync(booking, side, cancellationToken).ConfigureAwait(false);
        await notifications.CancelledAsync(booking, response, cancellationToken).ConfigureAwait(false);

        return Result<BookingResponse>.Success(response);
    }
}
