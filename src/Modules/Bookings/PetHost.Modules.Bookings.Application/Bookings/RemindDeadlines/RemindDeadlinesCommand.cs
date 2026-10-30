using FluentValidation;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.RemindDeadlines;

/// <summary>
/// Uma rodada da tarefa em segundo plano: lembra quem está perto de perder um prazo da reserva — o
/// anfitrião com pedido sem resposta (<see cref="Booking.ResponseReminderLead"/> antes de expirar) e o
/// tutor com aceite sem a 1ª parcela paga (<see cref="Booking.PaymentReminderLead"/> antes). Uma vez
/// só cada um. Disparada sem usuário.
/// </summary>
public sealed record RemindDeadlinesCommand : ICommand<RemindDeadlinesResponse>
{
    /// <summary>Quantas reservas uma rodada olha, no máximo; o resto fica para a próxima.</summary>
    public const int BatchSize = 200;
}

/// <summary>Quantos lembretes a rodada mandou.</summary>
/// <param name="ResponseReminders">Anfitriões lembrados de responder o pedido.</param>
/// <param name="PaymentReminders">Tutores lembrados de pagar a 1ª parcela.</param>
public sealed record RemindDeadlinesResponse(int ResponseReminders, int PaymentReminders);

/// <summary>Sem entrada: nada a validar (§10 pede validador em todo command).</summary>
public sealed class RemindDeadlinesCommandValidator : AbstractValidator<RemindDeadlinesCommand>;

/// <summary>
/// A regra fica em <see cref="Booking.TakeResponseReminder"/> e <see cref="Booking.TakePaymentReminder"/>.
/// Grava a rodada (o lembrete fica marcado e não repete) e só depois manda os e-mails. Não vai para a
/// trilha: não muda o status nem o dinheiro.
/// </summary>
public sealed class RemindDeadlinesCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
    TimeProvider timeProvider) : ICommandHandler<RemindDeadlinesCommand, RemindDeadlinesResponse>
{
    public async Task<Result<RemindDeadlinesResponse>> HandleAsync(RemindDeadlinesCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var candidates = await bookingRepository
            .ListDeadlinesToRemindAsync(now, RemindDeadlinesCommand.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        var responses = candidates.Where(booking => booking.TakeResponseReminder(now)).ToList();
        var payments = candidates.Where(booking => booking.TakePaymentReminder(now)).ToList();

        if (responses.Count == 0 && payments.Count == 0)
            return Result<RemindDeadlinesResponse>.Success(new RemindDeadlinesResponse(0, 0));

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<RemindDeadlinesResponse>.FromFailure(saved);

        foreach (var booking in responses)
        {
            var response = await responseBuilder.BuildAsync(booking, BookingParty.Host, cancellationToken).ConfigureAwait(false);
            await notifications.ResponseReminderAsync(booking, response, cancellationToken).ConfigureAwait(false);
        }

        foreach (var booking in payments)
        {
            var response = await responseBuilder.BuildAsync(booking, BookingParty.Owner, cancellationToken).ConfigureAwait(false);
            await notifications.PaymentReminderAsync(booking, response, cancellationToken).ConfigureAwait(false);
        }

        return Result<RemindDeadlinesResponse>.Success(new RemindDeadlinesResponse(responses.Count, payments.Count));
    }
}
