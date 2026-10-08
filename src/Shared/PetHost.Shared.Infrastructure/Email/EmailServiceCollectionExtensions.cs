using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>Registro do envio de e-mail: fila, despachante em segundo plano e SMTP.</summary>
public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();

        services.AddSingleton<EmailOutbox>();
        services.AddSingleton<IEmailOutbox>(provider => provider.GetRequiredService<EmailOutbox>());
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddHostedService<EmailDispatcher>();

        return services;
    }
}
