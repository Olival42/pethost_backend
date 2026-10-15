using FluentValidation;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.CloseOverdueStays;

/// <summary>
/// Uma rodada da tarefa em segundo plano para as estadias que já deviam ter acabado: reserva paga
/// em que o pet não chegou é cancelada (50% de reembolso), o anfitrião que não fez o check-out é
/// lembrado no dia de saída e, 3 dias depois, a plataforma faz o check-out. Disparada sem usuário.
/// </summary>
public sealed record CloseOverdueStaysCommand : ICommand<CloseOverdueStaysResponse>
{
    /// <summary>Quantas reservas uma rodada olha, no máximo; o resto fica para a próxima.</summary>
    public const int BatchSize = 200;
}

/// <summary>O que a rodada fez.</summary>
/// <param name="NoShows">Reservas pagas em que o pet não chegou, canceladas.</param>
/// <param name="Reminded">Anfitriões lembrados do check-out.</param>
/// <param name="AutoCheckedOut">Estadias concluídas pela plataforma.</param>
public sealed record CloseOverdueStaysResponse(int NoShows, int Reminded, int AutoCheckedOut);

/// <summary>Sem entrada: nada a validar (§10 pede validador em todo command).</summary>
public sealed class CloseOverdueStaysCommandValidator : AbstractValidator<CloseOverdueStaysCommand>;

/// <summary>
/// A regra fica em <see cref="Booking.CloseOverdueStay"/>. Grava a rodada inteira de uma vez
/// (conflito: a próxima rodada tenta de novo) e só depois: trilha, aviso ao pagamento (reembolso
/// do não comparecimento, repasse do check-out automático) e e-mails.
/// </summary>
public sealed class CloseOverdueStaysCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingPayments bookingPayments,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CloseOverdueStaysCommand, CloseOverdueStaysResponse>
{
    /// <summary>Valor de <c>cause</c> na trilha da reserva paga em que o pet não chegou.</summary>
    public const string NoShowCause = "no_show";

    /// <summary>Valor de <c>cause</c> na trilha do check-out feito pela plataforma.</summary>
    public const string AutoCheckOutCause = "auto_check_out";

    public async Task<Result<CloseOverdueStaysResponse>> HandleAsync(CloseOverdueStaysCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var overdue = await bookingRepository
            .ListOverdueStaysAsync(BookingClock.Today(now), CloseOverdueStaysCommand.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        var closed = overdue
            .Select(booking => (Booking: booking, Closing: booking.CloseOverdueStay(now)))
            .Where(item => item.Closing != StayClosing.None)
            .ToList();

        if (closed.Count == 0)
            return Result<CloseOverdueStaysResponse>.Success(new CloseOverdueStaysResponse(0, 0, 0));

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<CloseOverdueStaysResponse>.FromFailure(saved);

        foreach (var (booking, closing) in closed)
            await AnnounceAsync(booking, closing, cancellationToken).ConfigureAwait(false);

        return Result<CloseOverdueStaysResponse>.Success(new CloseOverdueStaysResponse(
            closed.Count(item => item.Closing == StayClosing.NoShow),
            closed.Count(item => item.Closing == StayClosing.CheckOutReminder),
            closed.Count(item => item.Closing == StayClosing.AutoCheckedOut)));
    }

    private async Task AnnounceAsync(Booking booking, StayClosing closing, CancellationToken cancellationToken)
    {
        var response = await responseBuilder.BuildAsync(booking, viewer: null, cancellationToken).ConfigureAwait(false);

        switch (closing)
        {
            case StayClosing.NoShow:
                await BookingTrail.RecordCancelledAsync(auditTrail, booking, cancellationToken, cause: NoShowCause).ConfigureAwait(false);
                await bookingPayments.BookingChangedAsync(booking.Id.Value, cancellationToken).ConfigureAwait(false);
                await notifications.CancelledAsync(booking, response, cancellationToken).ConfigureAwait(false);
                break;

            case StayClosing.CheckOutReminder:
                await notifications.CheckOutReminderAsync(booking, response, cancellationToken).ConfigureAwait(false);
                break;

            case StayClosing.AutoCheckedOut:
                await BookingTrail
                    .RecordAsync(
                        auditTrail,
                        AuditActions.BookingCheckedOut,
                        booking,
                        cancellationToken,
                        new Dictionary<string, string?> { ["by"] = BookingPartyValues.Platform, ["cause"] = AutoCheckOutCause })
                    .ConfigureAwait(false);
                await bookingPayments.BookingChangedAsync(booking.Id.Value, cancellationToken).ConfigureAwait(false);
                await notifications.AutoCheckedOutAsync(booking, response, cancellationToken).ConfigureAwait(false);
                break;
        }
    }
}
