using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>
/// <see cref="IPrivateImageStorage"/> sobre a API S3, no bucket <b>privado</b>
/// (<see cref="StorageOptions.PrivateBucketName"/>): mesmas credenciais e endpoint do bucket
/// público, mas sem política de leitura pública. Só a API, com a chave do servidor, lê.
/// </summary>
/// <remarks>
/// A chave é <c>&lt;pasta&gt;/&lt;guid&gt;.&lt;ext&gt;</c>, como no bucket público. O arquivo vai
/// com <c>Cache-Control: private, no-store</c>: nenhum cache intermediário guarda documento.
/// </remarks>
internal sealed partial class S3PrivateImageStorage(
    IAmazonS3 s3,
    IOptions<StorageOptions> options,
    ILogger<S3PrivateImageStorage> logger) : IPrivateImageStorage
{
    private const string CacheControl = "private, no-store";

    private readonly StorageOptions _options = options.Value;

    public async Task<Result<StoredPrivateImage>> SaveAsync(ImageUpload? image, string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

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
            DisablePayloadSigning = IsHttps,
        };
        request.Headers.CacheControl = CacheControl;

        await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        LogStored(logger, key, upload.Length);

        return Result<StoredPrivateImage>.Success(new StoredPrivateImage(key, upload.ContentHash(), upload.Length));
    }

    public async Task<PrivateImageContent?> OpenAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var response = await s3.GetObjectAsync(_options.PrivateBucketName, key, cancellationToken).ConfigureAwait(false);

            // O stream de resposta do SDK fica com a conexão: quem recebe o conteúdo fecha.
            return new PrivateImageContent(response.ResponseStream, response.Headers.ContentType ?? "application/octet-stream");
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

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

    private bool IsHttps => _options.ServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 6010, Level = LogLevel.Information, Message = "Private image stored at {Key} ({Bytes} bytes).")]
    private static partial void LogStored(ILogger logger, string key, long bytes);

    [LoggerMessage(EventId = 6011, Level = LogLevel.Information, Message = "Private image {Key} deleted.")]
    private static partial void LogDeleted(ILogger logger, string key);

    [LoggerMessage(EventId = 6012, Level = LogLevel.Warning, Message = "Could not delete private image {Key}; it stays orphaned in the bucket.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string key);
}
