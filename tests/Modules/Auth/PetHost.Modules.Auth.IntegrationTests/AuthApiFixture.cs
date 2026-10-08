using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetHost.Modules.Auth.Infrastructure;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using Respawn;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace PetHost.Modules.Auth.IntegrationTests;

/// <summary>
/// Sobe Postgres e Redis em container e a API em memória. Um container por
/// collection, banco limpo entre testes com Respawn (§14).
/// </summary>
public sealed class AuthApiFixture : IAsyncLifetime
{
    /// <summary>E-mail do admin que o seeder cria durante a subida da fixture.</summary>
    public const string SeededAdminEmail = "admin@pethost.test";

    public const string SeededAdminPassword = "senha-do-admin-123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pethost")
        .WithUsername("pethost")
        .WithPassword("pethost")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private WebApplicationFactory<Program>? _factory;
    private Respawner? _respawner;
    private DbConnection? _connection;

    public HttpClient CreateClient() => Factory.CreateClient();

    public IServiceProvider Services => Factory.Services;

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture was not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());

            builder.UseSetting("Jwt:Issuer", "https://api.pethost.test");
            builder.UseSetting("Jwt:Audience", "pethost-app");
            builder.UseSetting("Jwt:Key", "chave-de-teste-com-mais-de-32-bytes-para-hmac-sha256");
            builder.UseSetting("Jwt:AccessTokenLifetimeMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenLifetimeDays", "30");

            // Argon2 no custo mínimo: o default de 19 MiB por hash deixaria a
            // suíte de integração arrastada sem testar nada diferente.
            builder.UseSetting("Argon2:MemorySizeKib", "1024");
            builder.UseSetting("Argon2:Iterations", "1");
            builder.UseSetting("Argon2:DegreeOfParallelism", "1");

            builder.UseSetting("Seed:Admin:Enabled", "true");
            builder.UseSetting("Seed:Admin:Email", SeededAdminEmail);
            builder.UseSetting("Seed:Admin:Password", SeededAdminPassword);
            builder.UseSetting("Seed:Admin:FullName", "PetHost Admin");
        });

        // A WebApplicationFactory para o host no `builder.Build()`, então o
        // trecho final do Program.cs (migrations + seed) não roda sozinho aqui.
        await Services.InitializeAuthModuleAsync(applyMigrations: true);

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [AuthDbContext.Schema],
            TablesToIgnore = [new Respawn.Graph.Table(AuthDbContext.Schema, AuthDbContext.MigrationsHistoryTable)],
        });
    }

    /// <summary>
    /// Limpa as tabelas e recria o admin do seed, para cada teste começar do
    /// mesmo estado conhecido.
    /// </summary>
    public async Task ResetAsync()
    {
        if (_respawner is not null && _connection is not null)
            await _respawner.ResetAsync(_connection);

        await Services.InitializeAuthModuleAsync(applyMigrations: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _factory?.Dispose();

        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class AuthApiCollection : ICollectionFixture<AuthApiFixture>
{
    public const string Name = "auth-api";
}
