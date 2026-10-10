using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace PetHost.TestKit;

/// <summary>
/// Bucket de imagens para os testes de integração: um RustFS em container (o mesmo do compose,
/// compatível com S3), com leitura pública como o bucket de produção. Qualquer fixture que suba
/// a API usa este.
/// </summary>
/// <example>
/// <code>
/// await _bucket.StartAsync();
/// foreach (var (key, value) in _bucket.Settings)
///     builder.UseSetting(key, value);
/// </code>
/// </example>
public sealed class TestImageBucket : IAsyncDisposable
{
    public const string BucketName = "pethost-images";

    /// <summary>Mesma imagem do docker-compose.</summary>
    public const string Image = "rustfs/rustfs:1.0.1";

    private const int S3Port = 9000;
    private const string AccessKey = "pethost-test";
    private const string SecretKey = "pethost-test-secret";

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(S3Port, assignRandomHostPort: true)
        .WithEnvironment("RUSTFS_ACCESS_KEY", AccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", SecretKey)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(S3Port).ForPath("/health")))
        .Build();

    /// <summary>Endpoint S3 do container, visto do host dos testes.</summary>
    public string ServiceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(S3Port)}";

    /// <summary>Base pública das imagens: o começo de toda URL gravada no banco.</summary>
    public string PublicBaseUrl => $"{ServiceUrl}/{BucketName}";

    /// <summary>Configuração <c>Storage:*</c> da API apontando para este bucket.</summary>
    public IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Storage:ServiceUrl"] = ServiceUrl,
        ["Storage:Region"] = "us-east-1",
        ["Storage:AccessKeyId"] = AccessKey,
        ["Storage:SecretAccessKey"] = SecretKey,
        ["Storage:BucketName"] = BucketName,
        ["Storage:PublicBaseUrl"] = PublicBaseUrl,
        ["Storage:ForcePathStyle"] = "true",
    };

    /// <summary>
    /// Configuração <c>Storage:*</c> válida, mas sem container: para fixtures que não mexem com
    /// imagem e só precisam que a API suba.
    /// </summary>
    public static IReadOnlyDictionary<string, string> UnusedSettings { get; } = new Dictionary<string, string>
    {
        ["Storage:ServiceUrl"] = "http://storage.invalid",
        ["Storage:Region"] = "us-east-1",
        ["Storage:AccessKeyId"] = "unused",
        ["Storage:SecretAccessKey"] = "unused",
        ["Storage:BucketName"] = BucketName,
        ["Storage:PublicBaseUrl"] = "http://storage.invalid/" + BucketName,
    };

    public async Task StartAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        using var s3 = CreateClient();
        await s3.PutBucketAsync(BucketName).ConfigureAwait(false);
        await s3.PutBucketPolicyAsync(BucketName, PublicReadPolicy).ConfigureAwait(false);
    }

    /// <summary>A imagem existe no bucket? Recebe a URL pública gravada no banco.</summary>
    public async Task<bool> ExistsAsync(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        using var s3 = CreateClient();
        try
        {
            await s3.GetObjectMetadataAsync(BucketName, url[(PublicBaseUrl.Length + 1)..]).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Quantos arquivos há no bucket (para conferir que nada ficou órfão).</summary>
    public async Task<int> CountAsync()
    {
        using var s3 = CreateClient();
        var objects = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName }).ConfigureAwait(false);

        return objects.KeyCount ?? 0;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private AmazonS3Client CreateClient() =>
        new(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config
            {
                ServiceURL = ServiceUrl,
                AuthenticationRegion = "us-east-1",
                ForcePathStyle = true,
            });

    private static string PublicReadPolicy =>
        $$"""
        {
          "Version": "2012-10-17",
          "Statement": [{
            "Effect": "Allow",
            "Principal": { "AWS": ["*"] },
            "Action": ["s3:GetObject"],
            "Resource": ["arn:aws:s3:::{{BucketName}}/*"]
          }]
        }
        """;
}

/// <summary>Imagens mínimas válidas para os testes de upload.</summary>
public static class TestImages
{
    /// <summary>PNG de 1×1 pixel.</summary>
    public static byte[] Png { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>
    /// Um PNG diferente para cada <paramref name="variant"/>: o mesmo PNG com um byte a mais no
    /// fim, depois do bloco final (o arquivo continua sendo PNG). Para enviar várias fotos que
    /// não contem como repetidas.
    /// </summary>
    public static byte[] PngVariant(int variant) => [.. Png, (byte)variant];

    /// <summary>Começo de um JPEG (a assinatura é o que importa).</summary>
    public static byte[] Jpeg { get; } = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    /// <summary>Texto qualquer fingindo ser imagem.</summary>
    public static byte[] NotAnImage { get; } = "isto nao e uma imagem"u8.ToArray();
}
