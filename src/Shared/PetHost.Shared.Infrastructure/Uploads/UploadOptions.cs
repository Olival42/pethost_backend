using Microsoft.Extensions.Options;

namespace PetHost.Shared.Infrastructure.Uploads;

/// <summary>
/// Upload direto do navegador ao bucket (seção <c>Uploads</c>): quanto vale a URL assinada, o
/// prazo para confirmar e a limpeza do que ficou para trás.
/// </summary>
public sealed class UploadOptions
{
    public const string SectionName = "Uploads";

    public const int MaxUrlLifetimeMinutes = 60;

    public const int MaxConfirmWindowMinutes = 24 * 60;

    /// <summary>Quanto tempo a URL de <c>PUT</c> vale, em minutos (1 a 60). Padrão 10.</summary>
    public int UrlLifetimeMinutes { get; init; } = 10;

    /// <summary>
    /// Prazo para confirmar, contado do pedido (não menor que <see cref="UrlLifetimeMinutes"/>,
    /// até 24 h). Depois disso o upload vence e o arquivo é apagado. Padrão 30.
    /// </summary>
    public int ConfirmWindowMinutes { get; init; } = 30;

    /// <summary>Uploads pendentes (pedidos e não confirmados) por conta ao mesmo tempo. Padrão 10.</summary>
    public int MaxPendingPerUser { get; init; } = 10;

    /// <summary>De quanto em quanto tempo a limpeza roda, em minutos. Padrão 5.</summary>
    public int CleanupIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// Por quantas horas o upload concluído ou recusado fica guardado: é o que deixa a
    /// confirmação repetida devolver a mesma resposta. Padrão 24.
    /// </summary>
    public int FinishedRetentionHours { get; init; } = 24;

    /// <summary>
    /// Grava na subida o CORS do bucket privado (onde o navegador faz o <c>PUT</c>) com as origens
    /// de <c>Cors:AllowedOrigins</c>. Ligado no RustFS local; no R2 e no Supabase o CORS é
    /// configurado no painel (a chave de objeto não tem permissão para isso).
    /// </summary>
    public bool ConfigureBucketCors { get; init; }
}

/// <summary>Configuração fora dos limites derruba a subida.</summary>
internal sealed class UploadOptionsValidator : IValidateOptions<UploadOptions>
{
    public ValidateOptionsResult Validate(string? name, UploadOptions options)
    {
        var failures = new List<string>();

        if (options.UrlLifetimeMinutes is < 1 or > UploadOptions.MaxUrlLifetimeMinutes)
            failures.Add($"{UploadOptions.SectionName}:{nameof(UploadOptions.UrlLifetimeMinutes)} must be between 1 and {UploadOptions.MaxUrlLifetimeMinutes}.");

        if (options.ConfirmWindowMinutes < options.UrlLifetimeMinutes || options.ConfirmWindowMinutes > UploadOptions.MaxConfirmWindowMinutes)
            failures.Add($"{UploadOptions.SectionName}:{nameof(UploadOptions.ConfirmWindowMinutes)} must be between {nameof(UploadOptions.UrlLifetimeMinutes)} and {UploadOptions.MaxConfirmWindowMinutes}.");

        if (options.MaxPendingPerUser < 1)
            failures.Add($"{UploadOptions.SectionName}:{nameof(UploadOptions.MaxPendingPerUser)} must be at least 1.");

        if (options.CleanupIntervalMinutes < 1)
            failures.Add($"{UploadOptions.SectionName}:{nameof(UploadOptions.CleanupIntervalMinutes)} must be at least 1.");

        if (options.FinishedRetentionHours < 1)
            failures.Add($"{UploadOptions.SectionName}:{nameof(UploadOptions.FinishedRetentionHours)} must be at least 1.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
