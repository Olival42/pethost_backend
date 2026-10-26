using System.Text.Json;
using System.Transactions;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Uploads.Persistence;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Infrastructure.Uploads;

/// <summary>
/// Upload direto do navegador ao bucket, em dois passos: <see cref="RequestAsync"/> confere a
/// permissão e devolve uma URL de <c>PUT</c> assinada; <see cref="ConfirmAsync"/> confere o arquivo
/// que chegou e o aplica ao recurso pelo caso de uso de sempre (<see cref="IUploadPurpose"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>O arquivo vai para a área de espera do bucket <b>privado</b> (<see cref="UploadStaging"/>),
/// com chave gerada aqui: o cliente nunca escolhe chave nem URL. Só depois de conferido (formato
/// pelo conteúdo, tamanho, metadados removidos) é regravado no destino.</item>
/// <item>Confirmar de novo um upload concluído devolve a mesma resposta, sem aplicar outra vez;
/// duas confirmações ao mesmo tempo: uma pega (reserva no banco), a outra recebe 409.</item>
/// <item>Upload de outra conta (ou de outro papel da mesma conta): 404, como se não existisse.</item>
/// </list>
/// </remarks>
public sealed partial class UploadService
{
    /// <summary>Campo do texto da confirmação (a legenda da foto do chat).</summary>
    private const string ConfirmBodyField = "body";

    private static readonly string[] AcceptedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    private readonly Dictionary<string, IUploadPurpose> _purposes;
    private readonly IDbContextFactory<UploadsDbContext> _contextFactory;
    private readonly UploadStaging _staging;
    private readonly UploadOptions _options;
    private readonly JsonSerializerOptions _json;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UploadService> _logger;
    private readonly UploadEffects _effects;

    public UploadService(
        IEnumerable<IUploadPurpose> purposes,
        IDbContextFactory<UploadsDbContext> contextFactory,
        UploadStaging staging,
        IOptions<UploadOptions> options,
        IOptions<JsonOptions> jsonOptions,
        TimeProvider timeProvider,
        ILogger<UploadService> logger,
        UploadEffects effects)
    {
        _purposes = purposes.ToDictionary(purpose => purpose.Name, StringComparer.Ordinal);
        _contextFactory = contextFactory;
        _staging = staging;
        _options = options.Value;
        _json = jsonOptions.Value.JsonSerializerOptions;
        _timeProvider = timeProvider;
        _logger = logger;
        _effects = effects;
    }

    public async Task<Result<UploadTicketResponse>> RequestAsync(RequestUploadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<Error>();

        var purposeName = command.Purpose?.Trim().ToLowerInvariant();
        IUploadPurpose? purpose = null;
        if (string.IsNullOrEmpty(purposeName))
            errors.Add(UploadErrors.PurposeRequired);
        else if (!_purposes.TryGetValue(purposeName, out purpose))
            errors.Add(UploadErrors.PurposeInvalid);

        var contentType = command.ContentType?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(contentType))
            errors.Add(UploadErrors.ContentTypeRequired);
        else if (!AcceptedContentTypes.Contains(contentType))
            errors.Add(UploadErrors.ContentTypeInvalid);

        if (command.SizeBytes is null)
            errors.Add(UploadErrors.SizeRequired);
        else if (command.SizeBytes <= 0)
            errors.Add(UploadErrors.SizeInvalid);
        else if (command.SizeBytes > ImageRules.MaxBytes)
            errors.Add(UploadErrors.SizeTooLarge);

        var kind = command.Kind?.Trim().ToLowerInvariant();
        if (purpose is not null)
            errors.AddRange(ValidateTarget(purpose.Target, command.ResourceId, command.PhotoId, kind));

        if (errors.Count > 0)
            return Result<UploadTicketResponse>.Failure(errors);

        if (!purpose!.Roles.Contains(command.Role))
            return Result<UploadTicketResponse>.Failure(UploadErrors.PurposeForbidden);

        var now = _timeProvider.GetUtcNow();

        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var pending = await context.Sessions
                .CountAsync(
                    s => s.UserId == command.UserId
                        && (s.Status == UploadStatus.Pending || s.Status == UploadStatus.Processing)
                        && s.ConfirmExpiresAt > now,
                    cancellationToken)
                .ConfigureAwait(false);

            if (pending >= _options.MaxPendingPerUser)
                return Result<UploadTicketResponse>.Failure(UploadErrors.PendingLimitReached(_options.MaxPendingPerUser));

            var uploadId = Guid.CreateVersion7();

            // As mesmas checagens do caso de uso, sem gravar nada: estranho não ganha URL.
            var authorized = await purpose
                .AuthorizeAsync(new UploadPurposeContext(uploadId, command.UserId, command.Role, command.ResourceId, command.PhotoId, kind, Body: null), cancellationToken)
                .ConfigureAwait(false);

            if (authorized.IsFailure)
                return Result<UploadTicketResponse>.FromFailure(authorized);

            var session = new UploadSession
            {
                Id = uploadId,
                UserId = command.UserId,
                Role = command.Role,
                Purpose = purpose.Name,
                ResourceId = command.ResourceId,
                PhotoId = command.PhotoId,
                Kind = kind,
                DeclaredContentType = contentType!,
                DeclaredSizeBytes = command.SizeBytes!.Value,
                StagingKey = UploadStaging.KeyFor(uploadId),
                Status = UploadStatus.Pending,
                CreatedAt = now,
                UploadExpiresAt = now.AddMinutes(_options.UrlLifetimeMinutes),
                ConfirmExpiresAt = now.AddMinutes(_options.ConfirmWindowMinutes),
            };

            context.Sessions.Add(session);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var url = await _staging
                .SignPutAsync(session.StagingKey, session.DeclaredContentType, session.DeclaredSizeBytes, session.UploadExpiresAt)
                .ConfigureAwait(false);

            LogRequested(_logger, uploadId, session.Purpose, command.UserId);

            return Result<UploadTicketResponse>.Success(new UploadTicketResponse(
                uploadId,
                session.Purpose,
                url,
                "PUT",
                new Dictionary<string, string> { ["Content-Type"] = session.DeclaredContentType },
                session.DeclaredSizeBytes,
                session.UploadExpiresAt.ToUnixTimeSeconds(),
                session.ConfirmExpiresAt.ToUnixTimeSeconds()));
        }
    }

    public Task<Result<UploadConfirmationResponse>> ConfirmAsync(ConfirmUploadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return RunTransactionAsync(() => ConfirmLockedAsync(command, cancellationToken), commitFailure: true);
    }

    /// <summary>Atomically confirms selected host document slots, preserving all-or-nothing updates.</summary>
    public Task<Result<UploadConfirmationResponse>> ConfirmDocumentsAsync(Guid userId, string role, IReadOnlyList<Guid> uploadIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uploadIds);
        return RunTransactionAsync(async () =>
        {
            if (uploadIds.Count is < 1 or > 3 || uploadIds.Distinct().Count() != uploadIds.Count)
                return Result<UploadConfirmationResponse>.Failure(UploadErrors.DocumentBatchInvalid);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var sessions = await context.Sessions.AsNoTracking().Where(s => uploadIds.Contains(s.Id)
                && s.UserId == userId && s.Role == role && s.Purpose == "host-document").ToListAsync(cancellationToken).ConfigureAwait(false);
            if (sessions.Count != uploadIds.Count || sessions.Select(s => s.Kind).Distinct().Count() != sessions.Count)
                return Result<UploadConfirmationResponse>.Failure(UploadErrors.DocumentBatchInvalid);
            Result<UploadConfirmationResponse>? last = null;
            foreach (var id in uploadIds.Order())
            {
                last = await ConfirmLockedAsync(new ConfirmUploadCommand(userId, role, id, null), cancellationToken).ConfigureAwait(false);
                if (last.IsFailure)
                    return last;
            }
            return last!;
        }, commitFailure: false);
    }

    private async Task<Result<UploadConfirmationResponse>> RunTransactionAsync(Func<Task<Result<UploadConfirmationResponse>>> work, bool commitFailure)
    {
        _effects.Begin();
        var committing = false;
        try
        {
            Result<UploadConfirmationResponse> response;
            using (var transaction = new TransactionScope(
                TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromMinutes(5) },
                TransactionScopeAsyncFlowOption.Enabled))
            {
                response = await work().ConfigureAwait(false);
                // Contexts use the same PostgreSQL pool sequentially: never promote to a distributed transaction.
                if (Transaction.Current!.TransactionInformation.DistributedIdentifier != Guid.Empty)
                    throw new InvalidOperationException("Upload confirmation cannot use a distributed transaction.");
                if (response.IsSuccess || commitFailure)
                {
                    committing = true;
                    transaction.Complete();
                }
            }
            if (response.IsSuccess || commitFailure)
                await _effects.CommitAsync().ConfigureAwait(false);
            else
                await _effects.RollbackAsync().ConfigureAwait(false);
            return response;
        }
        catch (Exception exception) when (IsLockConflict(exception))
        {
            await _effects.RollbackAsync().ConfigureAwait(false);
            return Result<UploadConfirmationResponse>.Failure(UploadErrors.ConfirmationConflict);
        }
        catch
        {
            // A disconnected COMMIT may already have succeeded. Never delete a potentially committed photo.
            if (committing)
                _effects.Discard();
            else
                await _effects.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<Result<UploadConfirmationResponse>> ConfirmLockedAsync(ConfirmUploadCommand command, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // The row lock has no expiring lease: it lasts until commit/rollback and is released on connection loss.
        var session = await context.Sessions
            .FromSqlInterpolated($"SELECT * FROM uploads.upload_sessions WHERE id = {command.UploadId} AND user_id = {command.UserId} AND role = {command.Role} FOR UPDATE NOWAIT")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (session is null || !_purposes.TryGetValue(session.Purpose, out var purpose))
            return Result<UploadConfirmationResponse>.Failure(UploadErrors.NotFound);

        var purposeContext = new UploadPurposeContext(session.Id, session.UserId, session.Role, session.ResourceId, session.PhotoId, session.Kind, command.Body);
        if (session.Status == UploadStatus.Completed)
        {
            using var saved = JsonDocument.Parse(session.ResultJson!);
            var restored = await purpose.RestoreAsync(purposeContext, saved.RootElement, cancellationToken).ConfigureAwait(false);
            return restored.IsFailure
                ? Result<UploadConfirmationResponse>.FromFailure(restored)
                : Result<UploadConfirmationResponse>.Success(Response(session, JsonSerializer.Serialize(restored.Value!, _json)));
        }
        if (session.Status == UploadStatus.Rejected)
            return Result<UploadConfirmationResponse>.Failure(UploadErrors.AlreadyRejected);
        if (session.Status == UploadStatus.Expired)
            return Result<UploadConfirmationResponse>.Failure(UploadErrors.Expired);

        // Old deployments may have left a processing receipt; respect its lease during a rolling migration.
        if (session.Status == UploadStatus.Processing && session.LeaseUntil > _timeProvider.GetUtcNow())
            return Result<UploadConfirmationResponse>.Failure(UploadErrors.ConfirmationConflict);
        if (session.ConfirmExpiresAt <= _timeProvider.GetUtcNow())
        {
            await FinishAsync(context, session.Id, UploadStatus.Expired, null, null).ConfigureAwait(false);
            _effects.AfterCommit(ct => _staging.DeleteAsync(session.StagingKey, ct));
            return Result<UploadConfirmationResponse>.Failure(UploadErrors.Expired);
        }

        await using var content = await _staging.ReadAsync(session.StagingKey, ImageRules.MaxBytes, cancellationToken).ConfigureAwait(false);
        if (content is null)
            return Result<UploadConfirmationResponse>.Failure(UploadErrors.FileNotReceived);

        // A module can translate a database error into Result.Failure. Roll back its writes before
        // persisting a rejected receipt; do not accidentally commit partial work or a failed SQL transaction.
        await context.Database.ExecuteSqlRawAsync("SAVEPOINT upload_resource", cancellationToken).ConfigureAwait(false);
        var effectCheckpoint = _effects.Checkpoint();
        var completed = await purpose.CompleteAsync(
            purposeContext, new ImageUpload(content, FileName: null, session.DeclaredContentType), cancellationToken).ConfigureAwait(false);
        if (completed.IsFailure)
        {
            await context.Database.ExecuteSqlRawAsync("ROLLBACK TO SAVEPOINT upload_resource", cancellationToken).ConfigureAwait(false);
            await _effects.RollbackResourceAsync(effectCheckpoint).ConfigureAwait(false);
        }
        await context.Database.ExecuteSqlRawAsync("RELEASE SAVEPOINT upload_resource", cancellationToken).ConfigureAwait(false);
        if (completed.IsFailure && IsRetryable(completed))
            return Result<UploadConfirmationResponse>.FromFailure(completed);
        if (completed.IsFailure)
        {
            await FinishAsync(context, session.Id, UploadStatus.Rejected, null, completed.FirstError!.Reason ?? completed.FirstError.Code).ConfigureAwait(false);
            _effects.AfterCommit(ct => _staging.DeleteAsync(session.StagingKey, ct));
            return Result<UploadConfirmationResponse>.FromFailure(completed);
        }

        var json = JsonSerializer.Serialize(completed.Value!, completed.Value!.GetType(), _json);
        await FinishAsync(context, session.Id, UploadStatus.Completed, json, null).ConfigureAwait(false);
        _effects.AfterCommit(ct => _staging.DeleteAsync(session.StagingKey, ct));
        LogCompleted(_logger, session.Id, session.Purpose);
        return Result<UploadConfirmationResponse>.Success(Response(session, json));
    }

    private static bool IsLockConflict(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable }
        || (exception.InnerException is not null && IsLockConflict(exception.InnerException));

    private static IEnumerable<Error> ValidateTarget(UploadTargetRules rules, Guid? resourceId, Guid? photoId, string? kind)
    {
        if (rules.Resource is not null && (resourceId is null || resourceId == Guid.Empty))
            yield return UploadErrors.ResourceIdRequired(rules.Resource);
        else if (rules.Resource is null && resourceId is not null)
            yield return UploadErrors.ResourceIdNotAllowed;

        if (!rules.AllowsPhotoId && photoId is not null)
            yield return UploadErrors.PhotoIdNotAllowed;

        if (rules.Kinds is null)
        {
            if (!string.IsNullOrEmpty(kind))
                yield return UploadErrors.KindNotAllowed;
        }
        else if (string.IsNullOrEmpty(kind))
        {
            yield return UploadErrors.KindRequired;
        }
        else if (!rules.Kinds.Contains(kind))
        {
            yield return UploadErrors.KindInvalid.WithParams(("allowed", string.Join(", ", rules.Kinds)));
        }
    }

    /// <summary>
    /// Recusa que não é do arquivo nem do recurso, só do <c>body</c> da confirmação: o front corrige a
    /// legenda e confirma de novo, sem enviar o arquivo outra vez.
    /// </summary>
    private static bool IsRetryable(Result result) =>
        result.Errors!.All(error => error.Code == ErrorCodes.Validation && error.Field == ConfirmBodyField);

    private static UploadConfirmationResponse Response(UploadSession session, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new UploadConfirmationResponse(session.Id, session.Purpose, UploadStatus.Completed, document.RootElement.Clone());
    }

    private Task<int> FinishAsync(UploadsDbContext context, Guid id, string status, string? resultJson, string? failureCode)
    {
        var now = _timeProvider.GetUtcNow();
        var code = failureCode is { Length: > UploadSession.FailureCodeMaxLength } ? failureCode[..UploadSession.FailureCodeMaxLength] : failureCode;

        return context.Sessions
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Status, status)
                    .SetProperty(s => s.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(s => s.CompletedAt, now)
                    .SetProperty(s => s.ResultJson, resultJson)
                    .SetProperty(s => s.FailureCode, code),
                CancellationToken.None);
    }

    [LoggerMessage(EventId = 6030, Level = LogLevel.Information, Message = "Upload {UploadId} ({Purpose}) requested by {UserId}.")]
    private static partial void LogRequested(ILogger logger, Guid uploadId, string purpose, Guid userId);

    [LoggerMessage(EventId = 6031, Level = LogLevel.Information, Message = "Upload {UploadId} ({Purpose}) confirmed.")]
    private static partial void LogCompleted(ILogger logger, Guid uploadId, string purpose);

}

/// <summary>Pedido de URL de upload.</summary>
/// <param name="UserId">Do token.</param>
/// <param name="Role">Papel do token.</param>
public sealed record RequestUploadCommand(
    Guid UserId,
    string Role,
    string? Purpose,
    string? ContentType,
    long? SizeBytes,
    Guid? ResourceId,
    Guid? PhotoId,
    string? Kind);

/// <param name="Body">Legenda (só na foto do chat; ignorada nas outras finalidades).</param>
public sealed record ConfirmUploadCommand(Guid UserId, string Role, Guid UploadId, string? Body);

/// <summary>O que o front precisa para enviar o arquivo direto ao bucket.</summary>
/// <param name="UploadUrl">URL assinada de <c>PUT</c>. Vale até <paramref name="ExpiresAt"/>.</param>
/// <param name="Method">Sempre <c>PUT</c>.</param>
/// <param name="Headers">Headers obrigatórios do <c>PUT</c> (o <c>Content-Type</c> declarado). Outro valor: 403 no bucket.</param>
/// <param name="SizeBytes">O corpo do <c>PUT</c> precisa ter exatamente este tamanho (vai na assinatura).</param>
/// <param name="ExpiresAt">Vencimento da URL, em Unix time (segundos, UTC).</param>
/// <param name="ConfirmBy">Prazo para confirmar, em Unix time (segundos, UTC). Depois disso o upload vence.</param>
public sealed record UploadTicketResponse(
    Guid UploadId,
    string Purpose,
    string UploadUrl,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    long SizeBytes,
    long ExpiresAt,
    long ConfirmBy);

/// <summary>Upload confirmado.</summary>
/// <param name="Status">Sempre <c>completed</c>.</param>
/// <param name="Result">A resposta do caso de uso da finalidade: o tutor, o anfitrião, o pet, o cantinho, a mensagem ou a solicitação.</param>
public sealed record UploadConfirmationResponse(Guid UploadId, string Purpose, string Status, JsonElement Result);
