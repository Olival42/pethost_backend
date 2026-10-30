using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Payments.Application.Payments.RunPaymentsMaintenance;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Payments.Infrastructure.Maintenance;

/// <summary>
/// Tarefa em segundo plano: a cada <see cref="PaymentsOptions.MaintenanceIntervalMinutes"/> roda
/// <see cref="RunPaymentsMaintenanceCommand"/> — webhook perdido, parcela vencida, lembrete,
/// reembolso ou repasse que falhou.
/// </summary>
/// <remarks>Falha numa rodada vai para o log e não derruba a API: a próxima rodada tenta de novo.</remarks>
internal sealed partial class PaymentsMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentsOptions> options,
    ILogger<PaymentsMaintenanceService> logger) : BackgroundService
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
                .GetRequiredService<ICommandHandler<RunPaymentsMaintenanceCommand, RunPaymentsMaintenanceResponse>>();

            var result = await handler.HandleAsync(new RunPaymentsMaintenanceCommand(), stoppingToken).ConfigureAwait(false);

            if (result.IsSuccess && result.Value!.Reviewed > 0)
                LogReviewed(logger, result.Value.Reviewed, result.Value.Failed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligando a API: nada a fazer.
        }
#pragma warning disable CA1031 // Uma rodada que falha não pode derrubar a API: log e tenta na próxima.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogMaintenanceFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 9400, Level = LogLevel.Information, Message = "Payments maintenance: {Reviewed} payments reviewed, {Failed} left for the next run.")]
    private static partial void LogReviewed(ILogger logger, int reviewed, int failed);

    [LoggerMessage(EventId = 9401, Level = LogLevel.Error, Message = "Payments maintenance failed; it will run again on the next tick.")]
    private static partial void LogMaintenanceFailed(ILogger logger, Exception exception);
}
