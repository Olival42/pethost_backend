using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace PetHost.Shared.Infrastructure.Redis;

/// <summary>
/// Conexão com o Redis, compartilhada por todos os módulos que precisam dele (sessão no Auth,
/// cache do geocodificador no Listings). Fica no <c>Shared</c> para nenhum módulo depender do
/// registro feito por outro.
/// </summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>Nome da connection string do Redis.</summary>
    public const string ConnectionName = "Redis";

    /// <summary>
    /// Registra um <see cref="IConnectionMultiplexer"/> por processo (a biblioteca recomenda
    /// reusar a conexão). Idempotente: cada módulo chama, o primeiro registra.
    /// </summary>
    public static IServiceCollection AddRedis(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionName}' is not configured.");

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));

        return services;
    }
}
