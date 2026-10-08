using System.Text;
using Microsoft.Extensions.Options;

namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Valida a configuração do JWT na subida da aplicação, não no primeiro login:
/// chave curta ou issuer vazio derrubam o processo com mensagem clara, em vez de
/// virarem 500 em produção.
/// </summary>
internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
            failures.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)} is required.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            failures.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)} is required.");

        if (string.IsNullOrWhiteSpace(options.Key))
        {
            failures.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.Key)} is required.");
        }
        else if (Encoding.UTF8.GetByteCount(options.Key) < JwtOptions.MinimumKeyBytes)
        {
            failures.Add(
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.Key)} must be at least " +
                $"{JwtOptions.MinimumKeyBytes} bytes for HMAC-SHA256.");
        }

        if (options.AccessTokenLifetimeMinutes <= 0)
        {
            failures.Add(
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.AccessTokenLifetimeMinutes)} must be greater than zero.");
        }

        if (options.RefreshTokenLifetimeDays <= 0)
        {
            failures.Add(
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.RefreshTokenLifetimeDays)} must be greater than zero.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
