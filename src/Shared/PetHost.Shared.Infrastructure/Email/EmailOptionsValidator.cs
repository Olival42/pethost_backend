using Microsoft.Extensions.Options;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>
/// SMTP sem host ou sem remetente derruba a subida, em vez de cada e-mail falhar
/// calado no log em produção.
/// </summary>
internal sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host))
            failures.Add($"{EmailOptions.SectionName}:{nameof(EmailOptions.Host)} is required.");

        if (options.Port is <= 0 or > 65535)
            failures.Add($"{EmailOptions.SectionName}:{nameof(EmailOptions.Port)} must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(options.FromAddress))
            failures.Add($"{EmailOptions.SectionName}:{nameof(EmailOptions.FromAddress)} is required.");

        if (!string.IsNullOrWhiteSpace(options.AppUrl)
            && (!Uri.TryCreate(options.AppUrl, UriKind.Absolute, out var appUri)
                || (appUri.Scheme != Uri.UriSchemeHttps && appUri.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(appUri.UserInfo)
                || !string.IsNullOrEmpty(appUri.Query)
                || !string.IsNullOrEmpty(appUri.Fragment)))
            failures.Add("Email:AppUrl must be an absolute HTTP(S) frontend URL without credentials, query or fragment.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
