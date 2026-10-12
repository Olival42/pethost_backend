using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.Hosts.RunPayoutsMaintenance;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Infrastructure.Payouts;

/// <summary>
/// Tarefa em segundo plano: a cada <see cref="HostPayoutsOptions.MaintenanceIntervalMinutes"/> roda
/// <see cref="RunPayoutsMaintenanceCommand"/> (relê no Stripe quem ainda não recebe e manda os
/// lembretes). Fecha o furo do webhook perdido.
/// </summary>
/// <remarks>Mesmo molde da limpeza das solicitações: falha vai para o log e a próxima rodada tenta de novo.</remarks>
internal sealed partial class HostPayoutsMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IOptions<HostPayoutsOptions> options,
    ILogger<HostPayoutsMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.MaintenanceEnabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.MaintenanceIntervalMinutes));

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
                .GetRequiredService<ICommandHandler<RunPayoutsMaintenanceCommand, RunPayoutsMaintenanceResponse>>();

            var result = await handler.HandleAsync(new RunPayoutsMaintenanceCommand(), stoppingToken).ConfigureAwait(false);

            if (result.IsSuccess && (result.Value!.Synced > 0 || result.Value.Failed > 0 || result.Value.Reminded > 0))
                LogRun(logger, result.Value.Synced, result.Value.Failed, result.Value.Reminded);
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

    [LoggerMessage(EventId = 7220, Level = LogLevel.Information, Message = "Payout setup maintenance: {Synced} accounts synced, {Failed} failed, {Reminded} reminders sent.")]
    private static partial void LogRun(ILogger logger, int synced, int failed, int reminded);

    [LoggerMessage(EventId = 7221, Level = LogLevel.Error, Message = "Payout setup maintenance failed; it will run again on the next tick.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
