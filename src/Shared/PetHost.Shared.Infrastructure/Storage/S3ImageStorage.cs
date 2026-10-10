using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>
/// <see cref="IImageStorage"/> sobre a API S3: Cloudflare R2 em produção, RustFS em
/// desenvolvimento e nos testes.
/// </summary>
/// <remarks>
/// A chave do arquivo é <c>&lt;pasta&gt;/&lt;guid&gt;.&lt;ext&gt;</c>: aleatória, nunca
/// reaproveitada e sem nada do nome original. Por isso o arquivo vai com cache de um ano
/// (<c>immutable</c>): a mesma URL nunca muda de conteúdo.
/// </remarks>
internal sealed partial class S3ImageStorage(
    IAmazonS3 s3,
    IOptions<StorageOptions> options,
    ILogger<S3ImageStorage> logger) : IImageStorage
{
    private const string CacheControl = "public, max-age=31536000, immutable";

    private readonly StorageOptions _options = options.Value;

    private string PublicPrefix => _options.PublicBaseUrl.TrimEnd('/') + "/";

    public async Task<Result<StoredImage>> SaveAsync(ImageUpload? image, string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (image is null)
            return Result<StoredImage>.Failure(ImageErrors.FileRequired);

        // Lê no máximo um byte além do limite: o bastante para saber que passou, sem
        // carregar um arquivo gigante inteiro na memória.
        using var buffer = new MemoryStream();
        var tooLarge = await CopyUpToAsync(image.Content, buffer, ImageRules.MaxBytes, cancellationToken).ConfigureAwait(false);

        if (tooLarge)
            return Result<StoredImage>.Failure(ImageErrors.FileTooLarge);

        if (buffer.Length == 0)
            return Result<StoredImage>.Failure(ImageErrors.FileEmpty);

        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, ImageSignature.HeaderLength));
        if (ImageSignature.Detect(header) is not { } format)
            return Result<StoredImage>.Failure(ImageErrors.UnsupportedFormat);

        var key = $"{folder.Trim('/')}/{Guid.CreateVersion7():N}{format.Extension}";
        buffer.Position = 0;

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = buffer,
            AutoCloseStream = false,
            ContentType = format.ContentType,

            // O R2 pede payload sem assinatura; só vale em HTTPS (o RustFS local é HTTP).
            DisablePayloadSigning = IsHttps,
        };
        request.Headers.CacheControl = CacheControl;

        await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        LogImageStored(logger, key, buffer.Length);

        var hash = Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));

        return Result<StoredImage>.Success(new StoredImage(PublicPrefix + key, hash));
    }

    public async Task DeleteAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(PublicPrefix, StringComparison.Ordinal))
            return;

        var key = url[PublicPrefix.Length..];

        try
        {
            await s3.DeleteObjectAsync(_options.BucketName, key, cancellationToken).ConfigureAwait(false);
            LogImageDeleted(logger, key);
        }
#pragma warning disable CA1031 // Arquivo órfão no bucket não pode derrubar o pedido: só log.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogImageDeleteFailed(logger, exception, key);
        }
    }

    private bool IsHttps => _options.ServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Copia até <paramref name="limit"/> bytes. Devolve <c>true</c> se a origem tinha mais.</summary>
    private static async Task<bool> CopyUpToAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return false;

            total += read;
            if (total > limit)
                return true;

            await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "Image stored at {Key} ({Bytes} bytes).")]
    private static partial void LogImageStored(ILogger logger, string key, long bytes);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "Image {Key} deleted.")]
    private static partial void LogImageDeleted(ILogger logger, string key);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Warning, Message = "Could not delete image {Key}; it stays orphaned in the bucket.")]
    private static partial void LogImageDeleteFailed(ILogger logger, Exception exception, string key);
}
