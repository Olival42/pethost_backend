using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PetHost.Shared.Infrastructure.Stripe;

/// <summary>
/// Registro do Stripe: o cliente da API (<see cref="IStripeClientProvider"/>) e o endpoint único
/// de webhooks. Recurso usado por mais de um módulo (§5): idempotente, cada módulo pode chamar.
/// </summary>
public static class StripeServiceCollectionExtensions
{
    public static IServiceCollection AddStripe(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<StripeOptions>().Bind(configuration.GetSection(StripeOptions.SectionName));
        services.TryAddSingleton<IStripeClientProvider, StripeClientProvider>();
        services.TryAddScoped<StripeWebhookProcessor>();

        return services;
    }

    /// <summary>Registra o controller do webhook (<c>POST /api/v1/stripe/webhooks</c>) no MVC.</summary>
    public static IMvcBuilder AddStripeWebhooks(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddApplicationPart(typeof(StripeServiceCollectionExtensions).Assembly);
    }
}
