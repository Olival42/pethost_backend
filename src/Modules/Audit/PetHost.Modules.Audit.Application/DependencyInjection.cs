using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace PetHost.Modules.Audit.Application;

/// <summary>Registro da camada Application do módulo Audit (§3).</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra os validadores do módulo. Os handlers são amarrados em
    /// <c>AddAuditInfrastructure</c>.
    /// </summary>
    public static IServiceCollection AddAuditApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining(
            typeof(DependencyInjection),
            ServiceLifetime.Scoped,
            includeInternalTypes: false);

        return services;
    }
}
