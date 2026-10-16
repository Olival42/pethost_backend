using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Presentation.Realtime;

namespace PetHost.Modules.Conversations.Presentation;

/// <summary>Registro da camada Presentation do módulo Conversations (§3).</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra o assembly como application part, para o MVC achar os controllers, e o aviso em
    /// tempo real (<see cref="IConversationRealtime"/>), que usa o hub. O SignalR em si é
    /// registrado pelo host (<c>AddRealtime</c>).
    /// </summary>
    public static IMvcBuilder AddConversationsPresentation(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<IConversationRealtime, SignalRConversationRealtime>();

        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }

    /// <summary>
    /// Mapeia o <see cref="ConversationsHub"/> em <see cref="ConversationsHub.Route"/>. A conexão
    /// fecha quando o access token expira: token revogado ou vencido não fica recebendo eventos.
    /// </summary>
    public static IEndpointRouteBuilder MapConversationsHub(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHub<ConversationsHub>(
            ConversationsHub.Route,
            options => options.CloseOnAuthenticationExpiration = true);

        return endpoints;
    }
}
