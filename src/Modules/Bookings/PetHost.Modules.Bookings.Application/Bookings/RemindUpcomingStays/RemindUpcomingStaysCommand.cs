using FluentValidation;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.RemindUpcomingStays;

/// <summary>
/// Uma rodada da tarefa em segundo plano: na véspera do check-in (horário de Brasília), tutor e
/// anfitrião das reservas confirmadas recebem um lembrete por e-mail, uma vez só. Disparada sem usuário.
/// </summary>
public sealed record RemindUpcomingStaysCommand : ICommand<int>
{
    /// <summary>Quantas reservas uma rodada lembra, no máximo; o resto fica para a próxima.</summary>
    public const int BatchSize = 200;
}

/// <summary>Sem entrada: nada a validar (§10 pede validador em todo command).</summary>
public sealed class RemindUpcomingStaysCommandValidator : AbstractValidator<RemindUpcomingStaysCommand>;

/// <summary>
/// A regra fica em <see cref="Booking.TakeCheckInReminder"/>. Grava a rodada (o lembrete fica
/// marcado e não repete) e só depois manda os e-mails. Devolve quantas reservas foram lembradas.
/// Não vai para a trilha: não muda o status nem o dinheiro.
/// </summary>
public sealed class RemindUpcomingStaysCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
    TimeProvider timeProvider) : ICommandHandler<RemindUpcomingStaysCommand, int>
{
    public async Task<Result<int>> HandleAsync(RemindUpcomingStaysCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var upcoming = await bookingRepository
            .ListCheckInsToRemindAsync(BookingClock.Today(now).AddDays(1), RemindUpcomingStaysCommand.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        var reminded = upcoming.Where(booking => booking.TakeCheckInReminder(now)).ToList();
        if (reminded.Count == 0)
            return Result<int>.Success(0);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<int>.FromFailure(saved);

        foreach (var booking in reminded)
        {
            var response = await responseBuilder.BuildAsync(booking, viewer: null, cancellationToken).ConfigureAwait(false);
            await notifications.CheckInReminderAsync(booking, response, cancellationToken).ConfigureAwait(false);
        }

        return Result<int>.Success(reminded.Count);
    }
}
