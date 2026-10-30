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

    /// <summary>Bucket privado (sem leitura pública): documentos da verificação do anfitrião.</summary>
    public const string PrivateBucketName = "pethost-documents";

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
        ["Storage:PrivateBucketName"] = PrivateBucketName,
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
        ["Storage:PrivateBucketName"] = PrivateBucketName,
        ["Storage:PublicBaseUrl"] = "http://storage.invalid/" + BucketName,
    };

    public async Task StartAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        using var s3 = CreateClient();
        await WhenReadyAsync(() => s3.PutBucketAsync(BucketName)).ConfigureAwait(false);
        await WhenReadyAsync(() => s3.PutBucketPolicyAsync(BucketName, PublicReadPolicy)).ConfigureAwait(false);

        // Sem política: só quem tem a chave lê, como no bucket de produção.
        await WhenReadyAsync(() => s3.PutBucketAsync(PrivateBucketName)).ConfigureAwait(false);
    }

    /// <summary>
    /// Com vários projetos de integração subindo containers ao mesmo tempo, o RustFS responde o
    /// <c>/health</c> antes de aceitar as primeiras requisições S3 (503). Tenta de novo por até ~15s.
    /// </summary>
    private static async Task WhenReadyAsync(Func<Task> request)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await request().ConfigureAwait(false);
                return;
            }
            catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable && attempt < 30)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Quantos arquivos há no bucket privado.</summary>
    public async Task<int> CountPrivateAsync()
    {
        using var s3 = CreateClient();
        var objects = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = PrivateBucketName }).ConfigureAwait(false);

        return objects.KeyCount ?? 0;
    }

    /// <summary>A URL que o arquivo teria se o bucket privado fosse público (para provar que não é).</summary>
    public Uri PrivateObjectUrl(string key) => new($"{ServiceUrl}/{PrivateBucketName}/{key}");

    /// <summary>A chave do arquivo de uma URL assinada do bucket privado (o caminho, sem a query).</summary>
    public static string PrivateKeyOf(string signedUrl)
    {
        ArgumentNullException.ThrowIfNull(signedUrl);

        var path = new Uri(signedUrl).AbsolutePath;
        var marker = $"/{PrivateBucketName}/";

        return Uri.UnescapeDataString(path[(path.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]);
    }

    /// <summary>Chaves dos arquivos no bucket privado.</summary>
    public async Task<IReadOnlyList<string>> ListPrivateKeysAsync()
    {
        using var s3 = CreateClient();
        var objects = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = PrivateBucketName }).ConfigureAwait(false);

        return [.. (objects.S3Objects ?? []).Select(o => o.Key)];
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
    /// Um PNG diferente para cada <paramref name="variant"/>: o mesmo PNG com um chunk privado
    /// (<c>vaRi</c>) antes do bloco final. Para enviar várias fotos que não contem como
    /// repetidas. Não pode ser byte solto depois do fim: a API descarta o que vem depois do
    /// <c>IEND</c> ao tirar os metadados, e as variantes virariam a mesma foto.
    /// </summary>
    public static byte[] PngVariant(int variant)
    {
        const int iendLength = 12;
        byte[] chunk = [0x00, 0x00, 0x00, 0x01, .. "vaRi"u8, (byte)variant];
        var crc = Crc32(chunk.AsSpan(4));

        return
        [
            .. Png.AsSpan(0, Png.Length - iendLength),
            .. chunk,
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc,
            .. Png.AsSpan(Png.Length - iendLength),
        ];
    }

    /// <summary>
    /// JPEG pequeno com a estrutura inteira (início, JFIF, tabelas, cabeçalho do quadro, scan e
    /// fim): a API confere a estrutura ao tirar os metadados e recusa JPEG cortado.
    /// </summary>
    public static byte[] Jpeg { get; } =
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, .. "JFIF\0"u8, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xDB, 0x00, 0x43, 0x00, .. Enumerable.Repeat((byte)0x01, 64),
        0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, .. new byte[15], 0x00,
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
        0x00,
        0xFF, 0xD9,
    ];

    /// <summary>CRC-32 do PNG (tipo + dados do chunk).</summary>
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }

    /// <summary>Texto qualquer fingindo ser imagem.</summary>
    public static byte[] NotAnImage { get; } = "isto nao e uma imagem"u8.ToArray();
}
