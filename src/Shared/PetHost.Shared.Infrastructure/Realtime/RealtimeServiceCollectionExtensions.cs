using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Shared.Infrastructure.Redis;
using StackExchange.Redis;

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
    /// Prefixo dos canais do backplane no Redis, no padrão das chaves do projeto
    /// (<c>pethost:&lt;módulo&gt;:...</c>). Separa os canais do SignalR de qualquer outro uso do Redis.
    /// </summary>
    public const string BackplaneChannelPrefix = "pethost:signalr";

    /// <summary>
    /// Registra o SignalR com o mesmo JSON da API (<paramref name="configureJson"/>: camelCase,
    /// nulo omitido, enum como texto), a identificação da conexão pela claim <c>sub</c> e o
    /// backplane no Redis.
    /// </summary>
    /// <remarks>
    /// <b>Backplane:</b> as conexões ficam na memória da instância que as aceitou. Com o Redis no
    /// meio, o <c>Clients.User(...)</c> de uma instância publica num canal e todas as instâncias
    /// entregam às conexões que têm: a mensagem gravada pela instância A chega a quem está
    /// conectado na B. A conexão do backplane é própria (o SignalR a descarta ao desligar, e a do
    /// <c>AddRedis</c> é compartilhada com o Auth), mas usa a mesma connection string.
    /// </remarks>
    public static IServiceCollection AddRealtime(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JsonSerializerOptions> configureJson)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configureJson);

        var redisConnectionString = configuration.GetConnectionString(RedisServiceCollectionExtensions.ConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{RedisServiceCollectionExtensions.ConnectionName}' is not configured.");

        // Antes do AddSignalR: ele registra o provedor padrão com TryAdd, que então não entra.
        services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();

        services.AddSignalR()
            .AddJsonProtocol(protocol => configureJson(protocol.PayloadSerializerOptions))
            .AddStackExchangeRedis(redisConnectionString, redis =>
                redis.Configuration.ChannelPrefix = RedisChannel.Literal(BackplaneChannelPrefix));

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
