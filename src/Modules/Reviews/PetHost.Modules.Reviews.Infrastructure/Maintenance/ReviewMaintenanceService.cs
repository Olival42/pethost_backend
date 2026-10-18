using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Reviews.Application.Reminders.SendReviewReminders;
using PetHost.Modules.Reviews.Application.Reviews.NotifyPublishedReviews;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Infrastructure.Maintenance;

/// <summary>
/// Tarefa em segundo plano: a cada <see cref="ReviewMaintenanceOptions.IntervalMinutes"/> roda
/// <see cref="SendReviewRemindersCommand"/> (lembra quem não avaliou a estadia concluída há
/// <see cref="ReviewMaintenanceOptions.ReminderDelayHours"/> horas) e
/// <see cref="NotifyPublishedReviewsCommand"/> (avisa o avaliado das avaliações publicadas no fim do
/// prazo, às cegas).
/// </summary>
/// <remarks>Falha numa rodada vai para o log e não derruba a API: a próxima tenta de novo.</remarks>
internal sealed partial class ReviewMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReviewMaintenanceOptions> options,
    ILogger<ReviewMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.IntervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var settings = options.Value;

            var reminders = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<SendReviewRemindersCommand, SendReviewRemindersResponse>>()
                .HandleAsync(new SendReviewRemindersCommand(settings.ReminderDelayHours, settings.BatchSize), stoppingToken)
                .ConfigureAwait(false);

            var notices = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<NotifyPublishedReviewsCommand, NotifyPublishedReviewsResponse>>()
                .HandleAsync(new NotifyPublishedReviewsCommand(settings.BatchSize), stoppingToken)
                .ConfigureAwait(false);

            var sent = reminders.IsSuccess ? reminders.Value!.EmailsSent : 0;
            var notified = notices.IsSuccess ? notices.Value!.Notified : 0;

            if (sent > 0 || notified > 0)
                LogDone(logger, sent, notified);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligando a API: nada a fazer.
        }
#pragma warning disable CA1031 // Uma rodada que falha não pode derrubar a API: log e tenta na próxima.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 9220, Level = LogLevel.Information, Message = "Reviews maintenance: {Reminders} reminders queued, {Notified} published reviews notified.")]
    private static partial void LogDone(ILogger logger, int reminders, int notified);

    [LoggerMessage(EventId = 9221, Level = LogLevel.Error, Message = "Reviews maintenance failed; it will run again on the next tick.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
