using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PetHost.Shared.Infrastructure.Email.Persistence;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>
/// Entrega em segundo plano o que está na caixa de saída persistente
/// (<c>email.outbox_emails</c>). Acorda quando um e-mail é enfileirado e, de qualquer forma,
/// a cada <see cref="PollInterval"/> — para as novas tentativas e para o que ficou de uma
/// subida anterior.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Pegar um lote é um único <c>UPDATE ... FOR UPDATE SKIP LOCKED</c>: conta a tentativa e
/// reserva o e-mail por <see cref="Lease"/>. Duas instâncias da API nunca pegam o mesmo e-mail;
/// se a API cair no meio do envio, a reserva vence e outro despachante tenta de novo.</item>
/// <item>Entregou: a linha é apagada (o corpo pode ter token de troca de senha — não fica no banco).</item>
/// <item>Falhou: nova tentativa com espera dobrando (30 s, 1 min, 2 min… até 1 h). Depois de
/// <see cref="MaxAttempts"/> o e-mail é marcado como falho, sai da fila e é apagado depois de
/// <see cref="FailedRetention"/>.</item>
/// </list>
/// A garantia é "pelo menos uma vez": se a API cair entre o SMTP aceitar e a linha ser apagada,
/// aquele e-mail sai de novo.
/// </remarks>
internal sealed partial class EmailDispatcher(
    EmailOutbox outbox,
    IDbContextFactory<EmailDbContext> contextFactory,
    IEmailSender sender,
    TimeProvider timeProvider,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    public const int MaxAttempts = 8;

    private const int BatchSize = 20;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailedRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private DateTimeOffset _nextCleanupAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanUpIfDueAsync(stoppingToken).ConfigureAwait(false);

                // Lote cheio: pode haver mais na vez. Esvazia antes de dormir.
                while (await DispatchBatchAsync(stoppingToken).ConfigureAwait(false) == BatchSize)
                {
                }

                await WaitForWorkAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Banco fora do ar não pode matar o despachante: log e tenta na próxima volta.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogDispatchFailed(logger, exception);
                await Task.Delay(PollInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Pega e entrega um lote. Devolve quantos e-mails pegou.</summary>
    private async Task<int> DispatchBatchAsync(CancellationToken stoppingToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + Lease;

        List<OutboxEmail> batch;
        var context = await contextFactory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            batch = await context.Outbox
                .FromSql($"""
                    UPDATE email.outbox_emails
                    SET attempts = attempts + 1, next_attempt_at = {leaseUntil}
                    WHERE id IN (
                        SELECT id FROM email.outbox_emails
                        WHERE failed_at IS NULL AND next_attempt_at <= {now}
                        ORDER BY next_attempt_at
                        LIMIT {BatchSize}
                        FOR UPDATE SKIP LOCKED)
                    RETURNING *
                    """)
                .AsNoTracking()
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);
        }

        foreach (var email in batch)
            await DeliverAsync(email, stoppingToken).ConfigureAwait(false);

        return batch.Count;
    }

    private async Task DeliverAsync(OutboxEmail email, CancellationToken stoppingToken)
    {
        var maskedTo = Mask(email.ToAddress);

        try
        {
            await sender.SendAsync(email.ToMessage(), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligando: a reserva vence e o e-mail é tentado de novo na próxima subida.
            throw;
        }
#pragma warning disable CA1031 // Qualquer falha de SMTP vira nova tentativa; o laço tem de continuar.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            await ScheduleRetryAsync(email, exception, maskedTo).ConfigureAwait(false);
            return;
        }

        // CancellationToken.None: o SMTP já aceitou; apagar a linha não pode ficar pela metade.
        var context = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            await context.Outbox
                .Where(e => e.Id == email.Id)
                .ExecuteDeleteAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }

        LogSent(logger, maskedTo, email.Subject, email.Attempts);
    }

    private async Task ScheduleRetryAsync(OutboxEmail email, Exception exception, string maskedTo)
    {
        var now = timeProvider.GetUtcNow();
        var gaveUp = email.Attempts >= MaxAttempts;
        var nextAttemptAt = now + RetryDelay(email.Attempts);
        DateTimeOffset? failedAt = gaveUp ? now : null;

        var error = $"{exception.GetType().Name}: {exception.Message}";
        if (error.Length > OutboxEmail.LastErrorMaxLength)
            error = error[..OutboxEmail.LastErrorMaxLength];

        var context = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            await context.Outbox
                .Where(e => e.Id == email.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(e => e.NextAttemptAt, nextAttemptAt)
                        .SetProperty(e => e.LastError, error)
                        .SetProperty(e => e.FailedAt, failedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (gaveUp)
            LogGaveUp(logger, exception, maskedTo, email.Subject, email.Attempts);
        else
            LogRetryScheduled(logger, exception, maskedTo, email.Subject, email.Attempts, nextAttemptAt);
    }

    /// <summary>30 s depois da 1ª falha, dobrando a cada tentativa, no máximo 1 h.</summary>
    internal static TimeSpan RetryDelay(int attempts)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 16);
        var delay = FirstRetryDelay * Math.Pow(2, exponent);

        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    private async Task CleanUpIfDueAsync(CancellationToken stoppingToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now < _nextCleanupAt)
            return;

        _nextCleanupAt = now + CleanupInterval;
        var cutoff = now - FailedRetention;

        var context = await contextFactory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var deleted = await context.Outbox
                .Where(e => e.FailedAt != null && e.FailedAt < cutoff)
                .ExecuteDeleteAsync(stoppingToken)
                .ConfigureAwait(false);

            if (deleted > 0)
                LogFailedPurged(logger, deleted);
        }
    }

    /// <summary>Dorme até chegar e-mail novo ou passar <see cref="PollInterval"/>.</summary>
    private async Task WaitForWorkAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(PollInterval);

        try
        {
            await outbox.WakeUp.ReadAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Deu o tempo da varredura: segue para a próxima volta.
        }
    }

    /// <summary>E-mail em log vai mascarado (§15): <c>a***@exemplo.com</c>.</summary>
    private static string Mask(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);

        return at <= 0
            ? "***"
            : string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Email '{Subject}' sent to {To} (attempt {Attempt}).")]
    private static partial void LogSent(ILogger logger, string to, string subject, int attempt);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Could not send email '{Subject}' to {To} (attempt {Attempt}); retrying at {NextAttemptAt}.")]
    private static partial void LogRetryScheduled(ILogger logger, Exception exception, string to, string subject, int attempt, DateTimeOffset nextAttemptAt);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error,
        Message = "Gave up on email '{Subject}' to {To} after {Attempt} attempts.")]
    private static partial void LogGaveUp(ILogger logger, Exception exception, string to, string subject, int attempt);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error,
        Message = "Email outbox could not be read; trying again on the next tick.")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information,
        Message = "Deleted {Count} failed emails past the retention period.")]
    private static partial void LogFailedPurged(ILogger logger, int count);
}
