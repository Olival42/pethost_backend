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

namespace PetHost.Modules.Bookings.Application.Bookings.CheckOutBooking;

/// <summary>
/// O anfitrião marca "voltou pra casa": o pet está de novo com o tutor. A reserva vai de
/// <c>in_progress</c> para <c>completed</c> (libera repasse e avaliação). O admin também encerra a
/// estadia — o caminho para a de um anfitrião suspenso, que não entra mais.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="BookingId">Vem da rota.</param>
/// <param name="Role">Papel do token: <c>host</c> (o dono do cantinho) ou <c>admin</c> (qualquer reserva).</param>
/// <param name="Reason">Obrigatório para o admin (vai para a trilha); ignorado para o anfitrião.</param>
public sealed record CheckOutBookingCommand(Guid UserId, Guid BookingId, string Role = Roles.Host, string? Reason = null)
    : ICommand<BookingResponse>;

public sealed class CheckOutBookingCommandValidator : AbstractValidator<CheckOutBookingCommand>
{
    public CheckOutBookingCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithError(BookingsErrors.BookingIdRequired);

        RuleFor(x => x.Reason)
            .Cascade(CascadeMode.Stop)
            .Must(reason => !string.IsNullOrWhiteSpace(reason)).WithError(BookingsErrors.AdminReasonRequired)
            .MaximumLength(Booking.ReasonMaxLength).WithError(BookingsErrors.ReasonTooLong)
            .When(x => x.Role == Roles.Admin);
    }
}

/// <summary>
/// Só o anfitrião do cantinho, ativo, ou o admin; a regra fica em <see cref="Booking.CheckOut"/>.
/// Avisa o pagamento, que repassa ao anfitrião o que já foi pago. O check-out do admin vai para a
/// trilha com <c>by = platform</c> e o motivo.
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
            .LoadForHostOrAdminAsync(bookingRepository, bookingParties, command.UserId, command.Role, command.BookingId, cancellationToken)
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

        var byAdmin = command.Role == Roles.Admin;

        await BookingTrail
            .RecordAsync(
                auditTrail,
                AuditActions.BookingCheckedOut,
                booking,
                cancellationToken,
                byAdmin ? new Dictionary<string, string?> { ["by"] = BookingPartyValues.Platform } : null,
                byAdmin ? command.Reason!.Trim() : null)
            .ConfigureAwait(false);

        await bookingPayments.BookingChangedAsync(booking.Id.Value, cancellationToken).ConfigureAwait(false);

        var viewer = byAdmin ? (BookingParty?)null : BookingParty.Host;

        return Result<BookingResponse>.Success(await responseBuilder.BuildAsync(booking, viewer, cancellationToken).ConfigureAwait(false));
    }
}
