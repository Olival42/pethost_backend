using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.CreateSession;
using PetHost.Modules.Auth.Application.Sessions.RefreshSession;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Application.Sessions.RevokeSession;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.Modules.Auth.Infrastructure.Persistence.Repositories;
using PetHost.Modules.Auth.Infrastructure.Persistence.Seed;
using PetHost.Modules.Auth.Infrastructure.Security;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Primitives;
using StackExchange.Redis;

namespace PetHost.Modules.Auth.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Auth (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Nome da connection string do Postgres.</summary>
    public const string PostgresConnectionName = "Postgres";

    /// <summary>Nome da connection string do Redis.</summary>
    public const string RedisConnectionName = "Redis";

    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAuthOptions(configuration);
        services.AddAuthPersistence(configuration);
        services.AddAuthSecurity(configuration);
        services.AddAuthCommandHandlers();

        return services;
    }

    private static void AddAuthOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart: configuração errada derruba a subida, não o primeiro login.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.Configure<Argon2Options>(configuration.GetSection(Argon2Options.SectionName));
        services.Configure<AdminSeedOptions>(configuration.GetSection(AdminSeedOptions.SectionName));
    }

    private static void AddAuthPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<AuthDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(AuthDbContext.MigrationsHistoryTable, AuthDbContext.Schema))
            // PascalCase -> snake_case sem configurar coluna por coluna (§11).
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthUnitOfWork, AuthUnitOfWork>();
        services.AddScoped<AuthDbSeeder>();
    }

    private static void AddAuthSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString(RedisConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{RedisConnectionName}' is not configured.");

        // Um multiplexer por processo: a própria biblioteca recomenda reusar a conexão.
        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnectionString));

        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IAccessTokenGenerator, JwtAccessTokenGenerator>();
        services.AddSingleton<IRefreshTokenStore, RedisRefreshTokenStore>();
    }

    /// <summary>
    /// Os handlers são registrados aqui, e não na Application, porque o decorator
    /// de validação vive em <c>Shared.Infrastructure</c> e a Application não pode
    /// referenciar Infrastructure (§4).
    /// </summary>
    private static void AddAuthCommandHandlers(this IServiceCollection services)
    {
        services.AddValidatedCommandHandler<
            CreateSessionCommandHandler, CreateSessionCommand, SessionResponse>();

        services.AddValidatedCommandHandler<
            RefreshSessionCommandHandler, RefreshSessionCommand, SessionResponse>();

        services.AddValidatedCommandHandler<
            RevokeSessionCommandHandler, RevokeSessionCommand, Unit>();
    }
}
