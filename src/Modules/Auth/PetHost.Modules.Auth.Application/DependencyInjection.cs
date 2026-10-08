using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace PetHost.Modules.Auth.Application;

/// <summary>Registro da camada Application do módulo Auth (§3).</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra os validadores do módulo. Os handlers são amarrados em
    /// <c>AddAuthInfrastructure</c>, que é a camada com acesso ao decorator
    /// de validação (a Application não pode referenciar Infrastructure, §4).
    /// </summary>
    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining(
            typeof(DependencyInjection),
            ServiceLifetime.Scoped,
            includeInternalTypes: false);

        return services;
    }
}
