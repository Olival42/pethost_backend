using FluentValidation;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.CancelUnpaidBooking;

/// <summary>
/// Uma parcela venceu antes do check-in e passou da tolerância de 24h: a plataforma cancela a
/// reserva confirmada, com 100% de reembolso. Disparada sem usuário pelo módulo de pagamento
/// (contrato <c>IBookingPaymentOutcomes</c>), que em seguida devolve o dinheiro.
/// </summary>
/// <returns><c>true</c> se cancelou agora; <c>false</c> se já estava cancelada.</returns>
public sealed record CancelUnpaidBookingCommand(Guid BookingId) : ICommand<bool>;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class CancelUnpaidBookingCommandValidator : AbstractValidator<CancelUnpaidBookingCommand>
{
    public CancelUnpaidBookingCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithError(BookingsErrors.BookingIdRequired);
    }
}

/// <summary>
/// A regra fica em <see cref="Booking.CancelUnpaid"/>. Cancelada agora: grava, registra
/// <c>booking.cancelled</c> (<c>by = platform</c>, 100%) e avisa os dois lados. Não avisa o
/// pagamento de volta: quem chamou foi ele.
/// </summary>
public sealed class CancelUnpaidBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CancelUnpaidBookingCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(CancelUnpaidBookingCommand command, CancellationToken cancellationToken)
    {
        var id = new BookingId(command.BookingId);
        var booking = await bookingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (booking is null)
            return Result<bool>.Failure(BookingsErrors.NotFound(id));

        var cancelled = booking.CancelUnpaid(timeProvider.GetUtcNow());
        if (cancelled.IsFailure || !cancelled.Value)
            return cancelled;

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<bool>.FromFailure(saved);

        await BookingTrail.RecordCancelledAsync(auditTrail, booking, cancellationToken).ConfigureAwait(false);

        var response = await responseBuilder.BuildAsync(booking, viewer: null, cancellationToken).ConfigureAwait(false);
        await notifications.CancelledAsync(booking, response, cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true);
    }
}
