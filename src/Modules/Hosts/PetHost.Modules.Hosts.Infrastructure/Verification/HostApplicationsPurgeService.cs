using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.HostApplications.PurgeHostApplications;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Infrastructure.Verification;

/// <summary>
/// Tarefa de limpeza em segundo plano: a cada <see cref="HostVerificationOptions.PurgeIntervalMinutes"/>
/// roda <see cref="PurgeHostApplicationsCommand"/> (rascunhos abandonados expiram; fotos com
/// prazo de guarda vencido são apagadas do bucket privado).
/// </summary>
/// <remarks>
/// Falha numa rodada vai para o log e não derruba a API: a próxima rodada tenta de novo. A
/// primeira rodada é logo na subida, para não esperar uma hora depois de um deploy.
/// </remarks>
internal sealed partial class HostApplicationsPurgeService(
    IServiceScopeFactory scopeFactory,
    IOptions<HostVerificationOptions> options,
    ILogger<HostApplicationsPurgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PurgeEnabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.PurgeIntervalMinutes));

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
                .GetRequiredService<ICommandHandler<PurgeHostApplicationsCommand, PurgeHostApplicationsResponse>>();

            var result = await handler.HandleAsync(new PurgeHostApplicationsCommand(), stoppingToken).ConfigureAwait(false);

            if (result.IsSuccess && (result.Value!.ExpiredDrafts > 0 || result.Value.PurgedApplications > 0))
                LogPurged(logger, result.Value.ExpiredDrafts, result.Value.PurgedApplications);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligando a API: nada a fazer.
        }
#pragma warning disable CA1031 // Uma rodada que falha não pode derrubar a API: log e tenta na próxima.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogPurgeFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 7000, Level = LogLevel.Information, Message = "Host applications cleanup: {Expired} drafts expired, {Purged} applications had their photos deleted.")]
    private static partial void LogPurged(ILogger logger, int expired, int purged);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Error, Message = "Host applications cleanup failed; it will run again on the next tick.")]
    private static partial void LogPurgeFailed(ILogger logger, Exception exception);
}
