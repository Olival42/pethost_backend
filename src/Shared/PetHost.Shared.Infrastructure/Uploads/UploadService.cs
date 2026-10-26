using System.Text.Json;
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
    /// <summary>Quanto uma confirmação segura o upload. Se a API cair no meio, outra pode pegar depois disso.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

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

    public UploadService(
        IEnumerable<IUploadPurpose> purposes,
        IDbContextFactory<UploadsDbContext> contextFactory,
        UploadStaging staging,
        IOptions<UploadOptions> options,
        IOptions<JsonOptions> jsonOptions,
        TimeProvider timeProvider,
        ILogger<UploadService> logger)
    {
        _purposes = purposes.ToDictionary(purpose => purpose.Name, StringComparer.Ordinal);
        _contextFactory = contextFactory;
        _staging = staging;
        _options = options.Value;
        _json = jsonOptions.Value.JsonSerializerOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Os nomes das finalidades registradas, para a documentação e os testes.</summary>
    public IReadOnlyCollection<string> PurposeNames => _purposes.Keys;

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

    public async Task<Result<UploadConfirmationResponse>> ConfirmAsync(ConfirmUploadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var session = await context.Sessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == command.UploadId, cancellationToken)
                .ConfigureAwait(false);

            if (session is null
                || session.UserId != command.UserId
                || session.Role != command.Role
                || !_purposes.TryGetValue(session.Purpose, out var purpose))
            {
                return Result<UploadConfirmationResponse>.Failure(UploadErrors.NotFound);
            }

            var now = _timeProvider.GetUtcNow();

            var finished = Finished(session);
            if (finished is not null)
                return finished;

            if (session.ConfirmExpiresAt <= now && !HoldsLease(session, now))
            {
                await ExpireAsync(context, session, now, cancellationToken).ConfigureAwait(false);
                return Result<UploadConfirmationResponse>.Failure(UploadErrors.Expired);
            }

            // A reserva: um UPDATE condicional. Só uma confirmação passa daqui.
            var claimed = await context.Sessions
                .Where(s => s.Id == session.Id
                    && (s.Status == UploadStatus.Pending || (s.Status == UploadStatus.Processing && s.LeaseUntil < now)))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.Status, UploadStatus.Processing)
                        .SetProperty(s => s.LeaseUntil, now + Lease),
                    cancellationToken)
                .ConfigureAwait(false);

            if (claimed == 0)
            {
                var current = await context.Sessions.AsNoTracking()
                    .FirstAsync(s => s.Id == session.Id, cancellationToken)
                    .ConfigureAwait(false);

                return Finished(current) ?? Result<UploadConfirmationResponse>.Failure(UploadErrors.ConfirmationConflict);
            }

            Result<object> completed;
            try
            {
                var size = await _staging.GetSizeAsync(session.StagingKey, cancellationToken).ConfigureAwait(false);
                var content = size is null
                    ? null
                    : await _staging.ReadAsync(session.StagingKey, ImageRules.MaxBytes, cancellationToken).ConfigureAwait(false);

                if (content is null)
                {
                    await ReleaseAsync(context, session.Id).ConfigureAwait(false);
                    return Result<UploadConfirmationResponse>.Failure(UploadErrors.FileNotReceived);
                }

                await using (content.ConfigureAwait(false))
                {
                    var purposeContext = new UploadPurposeContext(
                        session.Id, session.UserId, session.Role, session.ResourceId, session.PhotoId, session.Kind, command.Body);

                    // O tipo declarado vai só como informação: o caso de uso descobre o formato pelos bytes.
                    completed = await purpose
                        .CompleteAsync(purposeContext, new ImageUpload(content, FileName: null, session.DeclaredContentType), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // Falha inesperada (bucket ou banco fora): volta a pendente, o arquivo fica e dá para tentar de novo.
                await ReleaseAsync(context, session.Id).ConfigureAwait(false);
                throw;
            }

            if (completed.IsFailure && IsRetryable(completed))
            {
                // Só o texto da confirmação (a legenda) está errado: o arquivo continua valendo.
                await ReleaseAsync(context, session.Id).ConfigureAwait(false);
                return Result<UploadConfirmationResponse>.FromFailure(completed);
            }

            if (completed.IsFailure)
            {
                await FinishAsync(context, session.Id, UploadStatus.Rejected, resultJson: null, completed.FirstError!.Reason ?? completed.FirstError.Code).ConfigureAwait(false);
                await _staging.DeleteAsync(session.StagingKey, CancellationToken.None).ConfigureAwait(false);

                LogRejected(_logger, session.Id, session.Purpose, completed.FirstError.Reason ?? completed.FirstError.Code);
                return Result<UploadConfirmationResponse>.FromFailure(completed);
            }

            var json = JsonSerializer.Serialize(completed.Value!, completed.Value!.GetType(), _json);

            // Marca concluído antes de apagar a espera: se a API cair entre os dois, a limpeza apaga o arquivo.
            await FinishAsync(context, session.Id, UploadStatus.Completed, json, failureCode: null).ConfigureAwait(false);
            await _staging.DeleteAsync(session.StagingKey, CancellationToken.None).ConfigureAwait(false);

            LogCompleted(_logger, session.Id, session.Purpose);
            return Result<UploadConfirmationResponse>.Success(Response(session, json));
        }
    }

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

    /// <summary>A resposta de um upload que já terminou, ou <c>null</c> se ainda está em aberto.</summary>
    private static Result<UploadConfirmationResponse>? Finished(UploadSession session) => session.Status switch
    {
        UploadStatus.Completed => Result<UploadConfirmationResponse>.Success(Response(session, session.ResultJson!)),
        UploadStatus.Rejected => Result<UploadConfirmationResponse>.Failure(UploadErrors.AlreadyRejected),
        UploadStatus.Expired => Result<UploadConfirmationResponse>.Failure(UploadErrors.Expired),
        _ => null,
    };

    private static bool HoldsLease(UploadSession session, DateTimeOffset now) =>
        session.Status == UploadStatus.Processing && session.LeaseUntil > now;

    private static UploadConfirmationResponse Response(UploadSession session, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new UploadConfirmationResponse(session.Id, session.Purpose, UploadStatus.Completed, document.RootElement.Clone());
    }

    private async Task ExpireAsync(UploadsDbContext context, UploadSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await context.Sessions
            .Where(s => s.Id == session.Id && (s.Status == UploadStatus.Pending || (s.Status == UploadStatus.Processing && s.LeaseUntil < now)))
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, UploadStatus.Expired).SetProperty(s => s.LeaseUntil, (DateTimeOffset?)null), cancellationToken)
            .ConfigureAwait(false);

        await _staging.DeleteAsync(session.StagingKey, CancellationToken.None).ConfigureAwait(false);
    }

    // CancellationToken.None: o estado tem de voltar mesmo se o pedido caiu.
    private static Task<int> ReleaseAsync(UploadsDbContext context, Guid id) =>
        context.Sessions
            .Where(s => s.Id == id && s.Status == UploadStatus.Processing)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, UploadStatus.Pending).SetProperty(s => s.LeaseUntil, (DateTimeOffset?)null), CancellationToken.None);

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

    [LoggerMessage(EventId = 6032, Level = LogLevel.Information, Message = "Upload {UploadId} ({Purpose}) rejected: {Reason}.")]
    private static partial void LogRejected(ILogger logger, Guid uploadId, string purpose, string reason);
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
