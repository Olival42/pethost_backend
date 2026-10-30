using Microsoft.Extensions.DependencyInjection;

namespace PetHost.Modules.Payments.Presentation;

/// <summary>Registro da camada Presentation do módulo Payments (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Registra o assembly como application part, para o MVC achar os controllers.</summary>
    public static IMvcBuilder AddPaymentsPresentation(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
