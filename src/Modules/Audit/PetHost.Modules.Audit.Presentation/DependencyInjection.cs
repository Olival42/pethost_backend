using Microsoft.Extensions.DependencyInjection;

namespace PetHost.Modules.Audit.Presentation;

/// <summary>Registro da camada Presentation do módulo Audit (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Registra o assembly como application part, para o MVC achar os controllers.</summary>
    public static IMvcBuilder AddAuditPresentation(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
