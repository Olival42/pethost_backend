using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace PetHost.Modules.Reviews.Application;

/// <summary>Registro da camada Application do módulo Reviews (§3).</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra os validadores do módulo. Os handlers são amarrados em
    /// <c>AddReviewsInfrastructure</c>, que tem acesso ao decorator de validação.
    /// </summary>
    public static IServiceCollection AddReviewsApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining(
            typeof(DependencyInjection),
            ServiceLifetime.Scoped,
            includeInternalTypes: false);

        return services;
    }
}
