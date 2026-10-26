using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Owners.Presentation.Uploads;
using PetHost.Shared.Infrastructure.Uploads;

namespace PetHost.Modules.Owners.Presentation;

/// <summary>Registro da camada Presentation do módulo Owners (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Registra o assembly como application part, para o MVC achar os controllers.</summary>
    public static IMvcBuilder AddOwnersPresentation(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Upload direto ao bucket (/api/v1/uploads): o mesmo caso de uso do upload por multipart.
        builder.Services.AddScoped<IUploadPurpose, OwnerAvatarUploadPurpose>();

        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
