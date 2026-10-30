using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Conversations.Application.Messages.NotifyUnreadMessages;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Infrastructure.UnreadEmail;

/// <summary>
/// Tarefa em segundo plano: a cada <see cref="UnreadEmailOptions.IntervalMinutes"/> roda
/// <see cref="NotifyUnreadMessagesCommand"/> (e-mail para quem tem mensagem sem ler há
/// <see cref="UnreadEmailOptions.DelayMinutes"/> minutos).
/// </summary>
/// <remarks>Falha numa rodada vai para o log e não derruba a API: a próxima tenta de novo.</remarks>
internal sealed partial class UnreadEmailService(
    IServiceScopeFactory scopeFactory,
    IOptions<UnreadEmailOptions> options,
    ILogger<UnreadEmailService> logger) : BackgroundService
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
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<NotifyUnreadMessagesCommand, NotifyUnreadMessagesResponse>>();

            var settings = options.Value;
            var result = await handler
                .HandleAsync(new NotifyUnreadMessagesCommand(settings.DelayMinutes, settings.CooldownMinutes, settings.BatchSize), stoppingToken)
                .ConfigureAwait(false);

            if (result.IsSuccess && result.Value!.EmailsSent > 0)
                LogSent(logger, result.Value.EmailsSent);
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

    [LoggerMessage(EventId = 9010, Level = LogLevel.Information, Message = "Unread messages e-mail: {Sent} e-mails queued.")]
    private static partial void LogSent(ILogger logger, int sent);

    [LoggerMessage(EventId = 9011, Level = LogLevel.Error, Message = "Unread messages e-mail failed; it will run again on the next tick.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
