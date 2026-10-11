using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.Abstractions;

namespace PetHost.Modules.Hosts.Infrastructure.Verification;

/// <summary>
/// Prazos da verificação do anfitrião. Vem do ambiente como
/// <c>HostVerification__DocumentRetentionDays</c> etc.
/// </summary>
public sealed class HostVerificationOptions
{
    public const string SectionName = "HostVerification";

    /// <summary>
    /// Dias que as fotos ficam depois que a solicitação fecha. <c>0</c> (padrão) apaga na hora
    /// da decisão: é o que menos expõe a pessoa.
    /// </summary>
    public int DocumentRetentionDays { get; init; }

    /// <summary>Dias que um rascunho fica aberto antes de expirar (e perder as fotos).</summary>
    public int DraftLifetimeDays { get; init; } = 7;

    /// <summary>Liga a tarefa de limpeza em segundo plano. Os testes desligam e chamam a limpeza direto.</summary>
    public bool PurgeEnabled { get; init; } = true;

    /// <summary>De quantos em quantos minutos a tarefa de limpeza roda.</summary>
    public int PurgeIntervalMinutes { get; init; } = 60;
}

/// <summary>Configuração inválida derruba a subida.</summary>
internal sealed class HostVerificationOptionsValidator : IValidateOptions<HostVerificationOptions>
{
    public ValidateOptionsResult Validate(string? name, HostVerificationOptions options)
    {
        var failures = new List<string>();

        if (options.DocumentRetentionDays < 0)
            failures.Add($"{HostVerificationOptions.SectionName}:{nameof(HostVerificationOptions.DocumentRetentionDays)} must be zero or more.");

        if (options.DraftLifetimeDays <= 0)
            failures.Add($"{HostVerificationOptions.SectionName}:{nameof(HostVerificationOptions.DraftLifetimeDays)} must be greater than zero.");

        if (options.PurgeIntervalMinutes <= 0)
            failures.Add($"{HostVerificationOptions.SectionName}:{nameof(HostVerificationOptions.PurgeIntervalMinutes)} must be greater than zero.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

/// <summary>Adaptador da porta <see cref="IHostVerificationPolicy"/> sobre as opções.</summary>
internal sealed class HostVerificationPolicy(IOptions<HostVerificationOptions> options) : IHostVerificationPolicy
{
    public TimeSpan DocumentRetention => TimeSpan.FromDays(options.Value.DocumentRetentionDays);

    public TimeSpan DraftLifetime => TimeSpan.FromDays(options.Value.DraftLifetimeDays);
}
