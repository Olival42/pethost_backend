using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PetHost.Modules.Audit.Domain.Entries;
using PetHost.Modules.Audit.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Audit.Infrastructure.Trail;

/// <summary>
/// Adaptador do contrato <see cref="IAuditTrail"/> (§5). Completa o registro com o
/// contexto do request (quem, de que IP, trace id) e grava em <c>audit.audit_entries</c>.
/// </summary>
/// <remarks>
/// Cada registro usa um <see cref="AuditDbContext"/> novo, criado pela factory: assim
/// uma falha aqui não deixa nada pendurado no contexto do request, e o registro não
/// entra por engano na transação de outra coisa. Falhar ao gravar <b>nunca</b> derruba o
/// request — a operação já aconteceu — e o registro inteiro vai para o log como erro,
/// para não se perder. Todo registro também sai no log como informação.
/// </remarks>
internal sealed partial class AuditTrail(
    IDbContextFactory<AuditDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<AuditTrail> logger) : IAuditTrail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var http = httpContextAccessor.HttpContext;
        var actorId = record.ActorId ?? UserIdFrom(http);
        var actorRole = record.ActorId is null ? http?.User.FindFirst("role")?.Value : null;

        var entry = AuditEntry.Create(
            timeProvider.GetUtcNow(),
            record.Action,
            record.TargetType,
            record.TargetId,
            actorId,
            actorRole,
            record.Reason,
            record.Details?.Where(d => d.Value is not null).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal),
            http?.Connection.RemoteIpAddress?.ToString(),
            http?.TraceIdentifier);

        LogRecorded(logger, entry.Action, entry.TargetType, entry.TargetId, entry.ActorId);

        try
        {
            // CancellationToken.None: o fato já aconteceu; cancelar o request não pode apagar o registro dele.
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            dbContext.Entries.Add(entry);
            await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A trilha nunca derruba o request: qualquer falha vira log.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception, JsonSerializer.Serialize(entry, Json));
        }
    }

    private static Guid? UserIdFrom(HttpContext? http) =>
        Guid.TryParse(http?.User.FindFirst("sub")?.Value, out var id) ? id : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit {Action} on {TargetType} {TargetId} by {ActorId}")]
    private static partial void LogRecorded(ILogger logger, string action, string targetType, Guid? targetId, Guid? actorId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit entry could not be saved; keeping it in the log: {Entry}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string entry);
}
