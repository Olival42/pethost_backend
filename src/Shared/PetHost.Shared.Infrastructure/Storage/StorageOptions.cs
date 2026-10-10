using Microsoft.Extensions.Options;

namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>
/// Bucket de imagens compatível com S3. Vem do ambiente como <c>Storage__ServiceUrl</c>,
/// <c>Storage__BucketName</c> etc.
/// </summary>
/// <remarks>
/// Desenvolvimento: o RustFS do compose (<c>http://rustfs:9000</c>, <c>ForcePathStyle</c>).
/// Produção: Cloudflare R2 (<c>https://&lt;account-id&gt;.r2.cloudflarestorage.com</c>, região
/// <c>auto</c>). O código é o mesmo; muda só a configuração.
/// </remarks>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Endpoint da API S3. No R2: <c>https://&lt;account-id&gt;.r2.cloudflarestorage.com</c>.</summary>
    public string ServiceUrl { get; init; } = string.Empty;

    /// <summary>Região da assinatura. O R2 usa <c>auto</c>; o RustFS aceita <c>us-east-1</c>.</summary>
    public string Region { get; init; } = "auto";

    /// <summary>Nunca versionado.</summary>
    public string AccessKeyId { get; init; } = string.Empty;

    /// <summary>Nunca versionado.</summary>
    public string SecretAccessKey { get; init; } = string.Empty;

    public string BucketName { get; init; } = string.Empty;

    /// <summary>
    /// Base pública das imagens: o que vai antes da chave do arquivo na URL gravada no banco.
    /// No R2, o domínio público do bucket (<c>https://pub-xxxx.r2.dev</c> ou um domínio próprio);
    /// no RustFS, <c>http://localhost:9000/&lt;bucket&gt;</c>.
    /// </summary>
    public string PublicBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// <c>true</c> para o RustFS (<c>host/bucket/chave</c>). O R2 funciona com os dois.
    /// </summary>
    public bool ForcePathStyle { get; init; }
}

/// <summary>Configuração incompleta derruba a subida, em vez de cada upload falhar em produção.</summary>
internal sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var failures = new List<string>();

        RequireAbsoluteUrl(options.ServiceUrl, nameof(StorageOptions.ServiceUrl), failures);
        RequireAbsoluteUrl(options.PublicBaseUrl, nameof(StorageOptions.PublicBaseUrl), failures);
        Require(options.Region, nameof(StorageOptions.Region), failures);
        Require(options.AccessKeyId, nameof(StorageOptions.AccessKeyId), failures);
        Require(options.SecretAccessKey, nameof(StorageOptions.SecretAccessKey), failures);
        Require(options.BucketName, nameof(StorageOptions.BucketName), failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void Require(string? value, string field, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
            failures.Add($"{StorageOptions.SectionName}:{field} is required.");
    }

    private static void RequireAbsoluteUrl(string? value, string field, List<string> failures)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            failures.Add($"{StorageOptions.SectionName}:{field} must be an absolute http or https URL.");
    }
}
