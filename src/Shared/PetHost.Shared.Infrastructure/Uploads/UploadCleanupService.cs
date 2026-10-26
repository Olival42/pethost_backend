using Microsoft.EntityFrameworkCore;
using System.Transactions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Shared.Infrastructure.Uploads.Persistence;

namespace PetHost.Shared.Infrastructure.Uploads;

/// <summary>
/// Limpeza periódica dos uploads diretos, a cada <see cref="UploadOptions.CleanupIntervalMinutes"/>:
/// <list type="number">
/// <item>pendente que passou do prazo de confirmar vira <c>expired</c>, e o arquivo sai do bucket;</item>
/// <item>arquivo da área de espera mais velho que o prazo de confirmar (mais uma folga) é apagado
/// mesmo sem linha no banco: <c>PUT</c> repetido depois da confirmação, apagar que falhou antes;</item>
/// <item>upload terminado há mais de <see cref="UploadOptions.FinishedRetentionHours"/> sai do banco.</item>
/// </list>
/// Upload recusado já tem o arquivo apagado na hora da confirmação.
/// </summary>
public sealed partial class UploadCleanupService(
    IDbContextFactory<UploadsDbContext> contextFactory,
    UploadStaging staging,
    IOptions<UploadOptions> options,
    TimeProvider timeProvider,
    ILogger<UploadCleanupService> logger) : BackgroundService
{
    private const int BatchSize = 100;

    /// <summary>Folga da varredura do bucket além do prazo de confirmar: nada pendente tem arquivo tão velho.</summary>
    private static readonly TimeSpan SweepMargin = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.CleanupIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Banco ou bucket fora do ar não pode matar a limpeza: log e tenta na próxima volta.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogCleanupFailed(logger, exception);
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Uma volta da limpeza. Público para os testes.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            int expired;
            do
            {
                List<UploadSession> due;
                using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                {
                    due = await context.Sessions.FromSqlInterpolated($"SELECT * FROM uploads.upload_sessions WHERE confirm_expires_at <= {now} AND (status = 'pending' OR (status = 'processing' AND lease_until < {now})) ORDER BY confirm_expires_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
                        .AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                    var ids = due.Select(session => session.Id).ToArray();
                    await context.Sessions.Where(session => ids.Contains(session.Id))
                        .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.Status, UploadStatus.Expired)
                            .SetProperty(session => session.CompletedAt, now)
                            .SetProperty(session => session.LeaseUntil, (DateTimeOffset?)null), cancellationToken).ConfigureAwait(false);
                    transaction.Complete();
                }
                // Delete only after the expiration transaction committed; active confirmations were skipped.
                foreach (var session in due)
                    await staging.DeleteAsync(session.StagingKey, cancellationToken).ConfigureAwait(false);

                expired = due.Count;
                if (expired > 0)
                    LogExpired(logger, expired);
            }
            while (expired == BatchSize);

            var orphans = await staging
                .ListOlderThanAsync(now - TimeSpan.FromMinutes(options.Value.ConfirmWindowMinutes) - SweepMargin, cancellationToken)
                .ConfigureAwait(false);

            foreach (var key in orphans)
                await staging.DeleteAsync(key, cancellationToken).ConfigureAwait(false);

            if (orphans.Count > 0)
                LogOrphansDeleted(logger, orphans.Count);

            var finishedBefore = now - TimeSpan.FromHours(options.Value.FinishedRetentionHours);
            await context.Sessions
                .Where(s => (s.Status == UploadStatus.Completed || s.Status == UploadStatus.Rejected || s.Status == UploadStatus.Expired)
                    && (s.CompletedAt ?? s.CreatedAt) < finishedBefore)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 6040, Level = LogLevel.Information, Message = "{Count} pending upload(s) expired and removed from the bucket.")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(EventId = 6041, Level = LogLevel.Information, Message = "{Count} orphaned staged file(s) removed from the bucket.")]
    private static partial void LogOrphansDeleted(ILogger logger, int count);

    [LoggerMessage(EventId = 6042, Level = LogLevel.Warning, Message = "Upload cleanup failed; it will run again on the next interval.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
