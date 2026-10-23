using FluentValidation;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Validation;
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
            .NotEmpty().WithError(BookingsErrors.BookingIdRequired);

        RuleFor(x => x.Reason)
            .MaximumLength(Booking.ReasonMaxLength).WithError(BookingsErrors.ReasonTooLong);

        RuleFor(x => x.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason)).WithError(BookingsErrors.AdminReasonRequired)
            .When(x => x.Role == Roles.Admin);
    }
}

/// <summary>
/// Só o tutor que pediu, o anfitrião do cantinho ou o admin (qualquer reserva, como a plataforma,
/// com motivo). O reembolso (<c>refundPercentage</c>) sai da regra do domínio
/// (<see cref="Booking.Cancel"/>): 100% se o anfitrião ou o admin cancela ou se o tutor cancela com
/// 48h de antecedência; 50% depois disso; nenhum se nada foi pago. Avisa o pagamento (que devolve
/// o dinheiro e cancela as parcelas em aberto) e os dois lados por e-mail.
/// </summary>
public sealed class CancelBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingParties bookingParties,
    IBookingPayments bookingPayments,
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

        // O motivo do admin vai para a trilha (§15); o de tutor e anfitrião, texto livre, não.
        var adminReason = side == BookingParty.Platform ? booking.CancellationReason : null;

        await BookingTrail.RecordCancelledAsync(auditTrail, booking, cancellationToken, adminReason).ConfigureAwait(false);
        await bookingPayments.BookingChangedAsync(booking.Id.Value, cancellationToken).ConfigureAwait(false);

        var response = await responseBuilder.BuildAsync(booking, side == BookingParty.Platform ? null : side, cancellationToken).ConfigureAwait(false);
        await notifications.CancelledAsync(booking, response, cancellationToken).ConfigureAwait(false);

        return Result<BookingResponse>.Success(response);
    }
}
