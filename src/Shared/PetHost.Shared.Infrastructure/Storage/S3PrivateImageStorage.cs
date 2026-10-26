using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>
/// <see cref="IPrivateImageStorage"/> sobre a API S3, no bucket <b>privado</b>
/// (<see cref="StorageOptions.PrivateBucketName"/>): mesmas credenciais e endpoint do bucket
/// público, mas sem política de leitura pública. Ninguém abre por link fixo: quem tem permissão
/// recebe uma URL assinada, que vence em <see cref="StorageOptions.SignedUrlLifetimeMinutes"/>.
/// </summary>
/// <remarks>
/// A chave é <c>&lt;pasta&gt;/&lt;guid&gt;.&lt;ext&gt;</c>, como no bucket público. O arquivo vai
/// com <c>Cache-Control: private, no-store</c>: nenhum cache intermediário guarda documento, e o
/// navegador não reaproveita a imagem depois que a URL vence.
/// </remarks>
internal sealed partial class S3PrivateImageStorage(
    IAmazonS3 s3,
    [FromKeyedServices(StorageServiceCollectionExtensions.UrlSignerKey)] IAmazonS3 urlSigner,
    IOptions<StorageOptions> options,
    TimeProvider timeProvider,
    ILogger<S3PrivateImageStorage> logger,
    ICommitEffects effects) : IPrivateImageStorage
{
    private const string CacheControl = "private, no-store";

    private readonly StorageOptions _options = options.Value;

    public async Task<Result<StoredPrivateImage>> SaveAsync(ImageUpload? image, string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (image is { IsPreflight: true })
            return Result<StoredPrivateImage>.Failure(ImageErrors.PreflightPassed);

        var read = await ImageUploadReader.ReadAsync(image, cancellationToken).ConfigureAwait(false);
        if (read.IsFailure)
            return Result<StoredPrivateImage>.FromFailure(read);

        using var upload = read.Value!;
        var key = $"{folder.Trim('/')}/{Guid.CreateVersion7():N}{upload.Format.Extension}";

        var request = new PutObjectRequest
        {
            BucketName = _options.PrivateBucketName,
            Key = key,
            InputStream = upload.Buffer,
            AutoCloseStream = false,
            ContentType = upload.Format.ContentType,
            DisablePayloadSigning = UsesHttps,
        };
        request.Headers.CacheControl = CacheControl;

        await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        if (effects.IsActive)
            effects.AfterRollback(ct => DeleteAsync(key, ct));

        LogStored(logger, key, upload.Length);

        return Result<StoredPrivateImage>.Success(new StoredPrivateImage(key, upload.ContentHash(), upload.Length));
    }

    public async Task<SignedImageUrl?> GetSignedUrlAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Assinar não consulta o bucket: sem esta conferência, um arquivo já apagado ganharia
        // um link que só falha quando aberto.
        try
        {
            await s3.GetObjectMetadataAsync(_options.PrivateBucketName, key, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await SignUrlAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SignedImageUrl> SignUrlAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var expiresAt = timeProvider.GetUtcNow().AddMinutes(_options.SignedUrlLifetimeMinutes);

        var url = await urlSigner
            .GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = _options.PrivateBucketName,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = expiresAt.UtcDateTime,

                // Sem isto o SDK assina como HTTPS mesmo com endpoint HTTP (RustFS local).
                Protocol = IsHttps(_options.SigningServiceUrl) ? Protocol.HTTPS : Protocol.HTTP,
            })
            .ConfigureAwait(false);

        LogSigned(logger, key, expiresAt);

        return new SignedImageUrl(url, expiresAt);
    }

    public async Task DeleteAsync(string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (effects.IsActive)
        {
            effects.AfterCommit(ct => DeleteAsync(key, ct));
            return;
        }

        try
        {
            await s3.DeleteObjectAsync(_options.PrivateBucketName, key, cancellationToken).ConfigureAwait(false);
            LogDeleted(logger, key);
        }
#pragma warning disable CA1031 // Arquivo órfão no bucket não pode derrubar o pedido: só log.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDeleteFailed(logger, exception, key);
        }
    }

    private bool UsesHttps => IsHttps(_options.ServiceUrl);

    private static bool IsHttps(string url) => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 6010, Level = LogLevel.Information, Message = "Private image stored at {Key} ({Bytes} bytes).")]
    private static partial void LogStored(ILogger logger, string key, long bytes);

    [LoggerMessage(EventId = 6013, Level = LogLevel.Information, Message = "Signed URL issued for private image {Key}, valid until {ExpiresAt}.")]
    private static partial void LogSigned(ILogger logger, string key, DateTimeOffset expiresAt);

    [LoggerMessage(EventId = 6011, Level = LogLevel.Information, Message = "Private image {Key} deleted.")]
    private static partial void LogDeleted(ILogger logger, string key);

    [LoggerMessage(EventId = 6012, Level = LogLevel.Warning, Message = "Could not delete private image {Key}; it stays orphaned in the bucket.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string key);
}
