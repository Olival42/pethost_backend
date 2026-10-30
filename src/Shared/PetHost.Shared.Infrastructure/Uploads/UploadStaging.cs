using System.Globalization;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Shared.Infrastructure.Storage;

namespace PetHost.Shared.Infrastructure.Uploads;

/// <summary>
/// Área de espera dos uploads diretos: <c>uploads/pending/&lt;id&gt;</c> no bucket <b>privado</b>.
/// O navegador grava aqui pela URL assinada; nada daqui abre por link. Depois de conferido, o
/// arquivo é regravado (já limpo) no destino de sempre pelo caso de uso, e o daqui é apagado.
/// </summary>
public sealed partial class UploadStaging(
    IAmazonS3 s3,
    [FromKeyedServices(StorageServiceCollectionExtensions.UrlSignerKey)] IAmazonS3 urlSigner,
    IOptions<StorageOptions> options,
    ILogger<UploadStaging> logger)
{
    public const string Prefix = "uploads/pending/";

    private readonly StorageOptions _options = options.Value;

    public static string KeyFor(Guid uploadId) => $"{Prefix}{uploadId:N}";

    /// <summary>
    /// URL de <c>PUT</c> que só aceita <b>este</b> tamanho e <b>este</b> content-type: os dois vão
    /// na assinatura (<c>X-Amz-SignedHeaders</c>), e corpo de outro tamanho ou tipo dá 403 no bucket.
    /// O navegador põe o <c>Content-Length</c> sozinho; o front só manda o <c>Content-Type</c>.
    /// </summary>
    public Task<string> SignPutAsync(string key, string contentType, long sizeBytes, DateTimeOffset expiresAt)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.PrivateBucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            ContentType = contentType,

            // Sem isto o SDK assina como HTTPS mesmo com endpoint HTTP (RustFS local).
            Protocol = _options.SigningServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? Protocol.HTTPS : Protocol.HTTP,
        };
        request.Headers["Content-Length"] = sizeBytes.ToString(CultureInfo.InvariantCulture);

        return urlSigner.GetPreSignedURLAsync(request);
    }

    /// <summary>
    /// O arquivo inteiro na memória (até <paramref name="maxBytes"/> + 1 byte: o leitor da imagem
    /// recusa o que passar), ou <c>null</c> se sumiu entre a conferência e a leitura.
    /// </summary>
    public async Task<MemoryStream?> ReadAsync(string key, long maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await s3.GetObjectAsync(_options.PrivateBucketName, key, cancellationToken).ConfigureAwait(false);
            await using var body = response.ResponseStream;

            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while (buffer.Length <= maxBytes
                && (read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Apaga o arquivo da área de espera. Nunca lança: a limpeza periódica pega o que sobrar.</summary>
    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await s3.DeleteObjectAsync(_options.PrivateBucketName, key, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Arquivo que ficou na área de espera não pode derrubar o pedido: a limpeza pega depois.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDeleteFailed(logger, exception, key);
        }
    }

    /// <summary>
    /// Os arquivos da área de espera gravados antes de <paramref name="olderThan"/>. Pega o que
    /// nenhum upload pendente explica: <c>PUT</c> repetido depois da confirmação, upload abandonado
    /// cuja linha já saiu do banco.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = _options.PrivateBucketName, Prefix = Prefix };

        ListObjectsV2Response response;
        do
        {
            response = await s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

            keys.AddRange((response.S3Objects ?? [])
                .Where(item => item.LastModified is { } modified && new DateTimeOffset(modified.ToUniversalTime(), TimeSpan.Zero) < olderThan)
                .Select(item => item.Key));

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return keys;
    }

    /// <summary>
    /// CORS do bucket privado: só as origens do front fazem <c>PUT</c> pelo navegador, com o
    /// header <c>Content-Type</c> (o único que o front manda; o <c>Content-Length</c> o navegador põe).
    /// </summary>
    public async Task ConfigureCorsAsync(IReadOnlyList<string> origins, CancellationToken cancellationToken)
    {
        var rule = new CORSRule
        {
            AllowedOrigins = [.. origins],
            AllowedMethods = ["PUT"],
            AllowedHeaders = ["content-type"],
            ExposeHeaders = ["ETag"],
            MaxAgeSeconds = 3600,
        };

        await s3.PutCORSConfigurationAsync(
                new PutCORSConfigurationRequest
                {
                    BucketName = _options.PrivateBucketName,
                    Configuration = new CORSConfiguration { Rules = [rule] },
                },
                cancellationToken)
            .ConfigureAwait(false);

        var allowed = string.Join(", ", origins);
        LogCorsConfigured(logger, _options.PrivateBucketName, allowed);
    }

    [LoggerMessage(EventId = 6020, Level = LogLevel.Warning, Message = "Could not delete staged upload {Key}; the cleanup will retry.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(EventId = 6021, Level = LogLevel.Information, Message = "CORS of bucket {Bucket} set for PUT from {Origins}.")]
    private static partial void LogCorsConfigured(ILogger logger, string bucket, string origins);
}
