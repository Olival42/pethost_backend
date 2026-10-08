using Microsoft.Extensions.DependencyInjection;

namespace PetHost.Modules.Auth.Presentation;

/// <summary>Registro da camada Presentation do módulo Auth (§3).</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra o assembly como application part. Sem isto o MVC não encontra os
    /// controllers, porque eles não estão no assembly de entrada.
    /// </summary>
    public static IMvcBuilder AddAuthPresentation(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
