using Microsoft.Extensions.Options;

namespace PetHost.Shared.Infrastructure.Cors;

/// <summary>Confere <see cref="FrontendCorsOptions"/> no startup: origem malformada derruba a API antes de subir.</summary>
internal sealed class FrontendCorsOptionsValidator : IValidateOptions<FrontendCorsOptions>
{
    public ValidateOptionsResult Validate(string? name, FrontendCorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        foreach (var origin in options.AllowedOrigins)
        {
            if (!IsOrigin(origin))
            {
                failures.Add(
                    $"{FrontendCorsOptions.SectionName}:{nameof(FrontendCorsOptions.AllowedOrigins)} must contain only http or https origins " +
                    $"(scheme, host and optional port, no path), but got '{origin}'.");
            }
        }

        if (options.PreflightMaxAgeMinutes is < 0 or > FrontendCorsOptions.MaxPreflightMaxAgeMinutes)
        {
            failures.Add(
                $"{FrontendCorsOptions.SectionName}:{nameof(FrontendCorsOptions.PreflightMaxAgeMinutes)} must be between 0 and {FrontendCorsOptions.MaxPreflightMaxAgeMinutes}.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Origem como o navegador manda no header <c>Origin</c>: <c>http(s)://host[:porta]</c>. A
    /// barra no fim é tolerada (é tirada no registro); caminho, query, usuário e <c>*</c> não.
    /// </summary>
    internal static bool IsOrigin(string? origin) =>
        Uri.TryCreate(origin?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo)
        && !uri.Host.Contains('*', StringComparison.Ordinal);
}
