using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Bookings.Application.Bookings.CloseOverdueStays;
using PetHost.Modules.Bookings.Application.Bookings.ExpireOverdueBookings;
using PetHost.Modules.Bookings.Application.Bookings.RemindDeadlines;
using PetHost.Modules.Bookings.Application.Bookings.RemindUpcomingStays;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Bookings.Infrastructure.Expiration;

/// <summary>
/// Tarefa em segundo plano: a cada <see cref="BookingExpirationOptions.IntervalMinutes"/> roda
/// <see cref="ExpireOverdueBookingsCommand"/> (pedidos sem resposta em 24h viram <c>expired</c>) e
/// <see cref="CloseOverdueStaysCommand"/> (pet que não chegou, lembrete e check-out automático) e
/// <see cref="RemindUpcomingStaysCommand"/> (lembrete da véspera do check-in) e
/// <see cref="RemindDeadlinesCommand"/> (pedido sem resposta e 1ª parcela sem pagamento perto do fim do prazo).
/// </summary>
/// <remarks>
/// Falha numa rodada vai para o log e não derruba a API: a próxima rodada tenta de novo. A
/// primeira rodada é logo na subida. Aceitar ou recusar fora do prazo também expira o pedido na
/// hora, então o atraso da tarefa nunca deixa um pedido vencido ser aceito.
/// </remarks>
internal sealed partial class BookingExpirationService(
    IServiceScopeFactory scopeFactory,
    IOptions<BookingExpirationOptions> options,
    ILogger<BookingExpirationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.IntervalMinutes));

        do
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ExpireOverdueBookingsCommand, ExpireOverdueBookingsResponse>>();

            var result = await handler.HandleAsync(new ExpireOverdueBookingsCommand(), stoppingToken).ConfigureAwait(false);

            if (result.IsSuccess && result.Value!.Expired > 0)
                LogExpired(logger, result.Value.Expired);

            var stays = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<CloseOverdueStaysCommand, CloseOverdueStaysResponse>>()
                .HandleAsync(new CloseOverdueStaysCommand(), stoppingToken)
                .ConfigureAwait(false);

            if (stays.IsSuccess && stays.Value!.NoShows + stays.Value.Reminded + stays.Value.AutoCheckedOut > 0)
                LogStaysClosed(logger, stays.Value.NoShows, stays.Value.Reminded, stays.Value.AutoCheckedOut);

            var upcoming = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<RemindUpcomingStaysCommand, int>>()
                .HandleAsync(new RemindUpcomingStaysCommand(), stoppingToken)
                .ConfigureAwait(false);

            if (upcoming.IsSuccess && upcoming.Value > 0)
                LogCheckInsReminded(logger, upcoming.Value);

            var deadlines = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<RemindDeadlinesCommand, RemindDeadlinesResponse>>()
                .HandleAsync(new RemindDeadlinesCommand(), stoppingToken)
                .ConfigureAwait(false);

            if (deadlines.IsSuccess && deadlines.Value!.ResponseReminders + deadlines.Value.PaymentReminders > 0)
                LogDeadlinesReminded(logger, deadlines.Value.ResponseReminders, deadlines.Value.PaymentReminders);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligando a API: nada a fazer.
        }
#pragma warning disable CA1031 // Uma rodada que falha não pode derrubar a API: log e tenta na próxima.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogExpirationFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 8000, Level = LogLevel.Information, Message = "Booking expiration: {Expired} requests expired without an answer.")]
    private static partial void LogExpired(ILogger logger, int expired);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Information, Message = "Overdue stays: {NoShows} no-shows cancelled, {Reminded} hosts reminded to check out, {AutoCheckedOut} stays checked out by the platform.")]
    private static partial void LogStaysClosed(ILogger logger, int noShows, int reminded, int autoCheckedOut);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Information, Message = "Upcoming stays: {Reminded} bookings reminded on the eve of check-in.")]
    private static partial void LogCheckInsReminded(ILogger logger, int reminded);

    [LoggerMessage(EventId = 8004, Level = LogLevel.Information, Message = "Deadlines: {Responses} hosts reminded to answer, {Payments} owners reminded to pay the first installment.")]
    private static partial void LogDeadlinesReminded(ILogger logger, int responses, int payments);

    [LoggerMessage(EventId = 8001, Level = LogLevel.Error, Message = "Booking expiration failed; it will run again on the next tick.")]
    private static partial void LogExpirationFailed(ILogger logger, Exception exception);
}
