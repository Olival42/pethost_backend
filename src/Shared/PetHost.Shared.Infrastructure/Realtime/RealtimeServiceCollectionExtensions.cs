using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace PetHost.Shared.Infrastructure.Realtime;

/// <summary>
/// Tempo real (SignalR), compartilhado pelos módulos que têm hub (hoje, o Conversations). Fica no
/// <c>Shared</c> para nenhum módulo depender do registro feito por outro.
/// </summary>
/// <remarks>
/// O hub só <b>empurra</b> eventos do servidor para o cliente. Escrever (abrir conversa, enviar,
/// marcar como lida) é sempre REST: validação, envelope, rate limit e trilha ficam num lugar só.
/// </remarks>
public static class RealtimeServiceCollectionExtensions
{
    /// <summary>Prefixo de todo hub (<c>/hubs/conversations</c>...). Só nele o token vem pela query.</summary>
    public const string HubsPath = "/hubs";

    /// <summary>
    /// Parâmetro da query com o access token. O WebSocket do navegador não manda o header
    /// <c>Authorization</c>; o cliente do SignalR (<c>accessTokenFactory</c>) põe o token aqui.
    /// </summary>
    public const string AccessTokenQueryParameter = "access_token";

    /// <summary>
    /// Registra o SignalR com o mesmo JSON da API (<paramref name="configureJson"/>: camelCase,
    /// nulo omitido, enum como texto) e a identificação da conexão pela claim <c>sub</c>.
    /// </summary>
    public static IServiceCollection AddRealtime(this IServiceCollection services, Action<JsonSerializerOptions> configureJson)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureJson);

        // Antes do AddSignalR: ele registra o provedor padrão com TryAdd, que então não entra.
        services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();

        services.AddSignalR().AddJsonProtocol(protocol => configureJson(protocol.PayloadSerializerOptions));

        return services;
    }

    /// <summary>
    /// O access token da query, quando o pedido é para um hub. Fora de <see cref="HubsPath"/>, o
    /// token só vale no header: link com token na URL vazaria em log e histórico.
    /// </summary>
    public static string? ReadHubAccessToken(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Path.StartsWithSegments(HubsPath))
            return null;

        string? token = request.Query[AccessTokenQueryParameter];

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
