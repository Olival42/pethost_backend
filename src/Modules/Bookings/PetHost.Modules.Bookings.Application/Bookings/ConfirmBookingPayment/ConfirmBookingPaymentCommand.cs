using FluentValidation;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.ConfirmBookingPayment;

/// <summary>
/// A 1ª parcela foi paga (o módulo de pagamento recebeu a confirmação do Stripe): a reserva
/// aceita vira confirmada. Disparada sem usuário, pelo contrato <c>IBookingPaymentOutcomes</c>.
/// </summary>
/// <returns><c>true</c> se confirmou agora; <c>false</c> se já estava confirmada (aviso repetido).</returns>
public sealed record ConfirmBookingPaymentCommand(Guid BookingId) : ICommand<bool>;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class ConfirmBookingPaymentCommandValidator : AbstractValidator<ConfirmBookingPaymentCommand>
{
    public ConfirmBookingPaymentCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithError(BookingsErrors.BookingIdRequired);
    }
}

/// <summary>
/// A regra fica em <see cref="Booking.ConfirmPayment"/>. Confirmada agora: grava, registra
/// <c>booking.confirmed</c> e avisa o tutor (com o endereço e o telefone do anfitrião) e o
/// anfitrião (com o telefone do tutor). Reserva cancelada ou expirada enquanto o tutor pagava:
/// <c>422 BOOKING_NOT_AWAITING_PAYMENT</c> — o pagamento devolve o dinheiro.
/// </summary>
public sealed class ConfirmBookingPaymentCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ConfirmBookingPaymentCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(ConfirmBookingPaymentCommand command, CancellationToken cancellationToken)
    {
        var id = new BookingId(command.BookingId);
        var booking = await bookingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (booking is null)
            return Result<bool>.Failure(BookingsErrors.NotFound(id));

        var confirmed = booking.ConfirmPayment(timeProvider.GetUtcNow());
        if (confirmed.IsFailure || !confirmed.Value)
            return confirmed;

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<bool>.FromFailure(saved);

        await BookingTrail.RecordAsync(auditTrail, AuditActions.BookingConfirmed, booking, cancellationToken).ConfigureAwait(false);

        var response = await responseBuilder.BuildAsync(booking, viewer: null, cancellationToken).ConfigureAwait(false);
        await notifications.ConfirmedAsync(booking, response, cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true);
    }
}
