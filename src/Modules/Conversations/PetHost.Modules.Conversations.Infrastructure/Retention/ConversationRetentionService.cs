using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Infrastructure.Retention;

/// <summary>
/// Tarefa em segundo plano: a cada <see cref="ConversationRetentionOptions.IntervalMinutes"/> roda
/// <see cref="PurgeInactiveConversationsCommand"/> (conversas sem mensagem há
/// <see cref="ConversationRetentionOptions.InactivityDays"/> dias somem, com as fotos).
/// </summary>
/// <remarks>
/// Falha numa rodada vai para o log e não derruba a API: a próxima rodada tenta de novo. A
/// primeira rodada é logo na subida.
/// </remarks>
internal sealed partial class ConversationRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<ConversationRetentionOptions> options,
    ILogger<ConversationRetentionService> logger) : BackgroundService
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
                .GetRequiredService<ICommandHandler<PurgeInactiveConversationsCommand, PurgeInactiveConversationsResponse>>();

            var command = new PurgeInactiveConversationsCommand(options.Value.InactivityDays, options.Value.BatchSize);
            var result = await handler.HandleAsync(command, stoppingToken).ConfigureAwait(false);

            if (result.IsSuccess && result.Value!.Purged > 0)
                LogPurged(logger, result.Value.Purged);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligando a API: nada a fazer.
        }
#pragma warning disable CA1031 // Uma rodada que falha não pode derrubar a API: log e tenta na próxima.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogRetentionFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 9000, Level = LogLevel.Information, Message = "Conversation retention: {Purged} inactive conversations purged.")]
    private static partial void LogPurged(ILogger logger, int purged);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Error, Message = "Conversation retention failed; it will run again on the next tick.")]
    private static partial void LogRetentionFailed(ILogger logger, Exception exception);
}
