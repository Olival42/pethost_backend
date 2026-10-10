using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetHost.Modules.Audit.Infrastructure;
using PetHost.Modules.Audit.Infrastructure.Persistence;
using PetHost.Modules.Auth.Infrastructure;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.Modules.Owners.Infrastructure;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.Shared.Infrastructure.Email;
using Respawn;
using Respawn.Graph;
using PetHost.TestKit;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace PetHost.Modules.Owners.IntegrationTests;

/// <summary>
/// Sobe Postgres e Redis em container e a API em memória. O cadastro de tutor cruza
/// dois módulos, então a fixture aplica as migrations e limpa os schemas dos dois.
/// </summary>
public sealed class OwnersApiFixture : IAsyncLifetime
{
    /// <summary>Admin criado pelo seeder, para as rotas só de admin.</summary>
    public const string AdminEmail = "admin@pethost.test";

    public const string AdminPassword = "senha-do-admin-123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pethost")
        .WithUsername("pethost")
        .WithPassword("pethost")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly TestImageBucket _bucket = new();

    private WebApplicationFactory<Program>? _factory;
    private Respawner? _respawner;
    private DbConnection? _connection;

    public HttpClient CreateClient() => Factory.CreateClient();

    public IServiceProvider Services => Factory.Services;

    /// <summary>O bucket de imagens (MinIO) que a API usa.</summary>
    public TestImageBucket Bucket => _bucket;

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture was not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await _bucket.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());

            builder.UseSetting("Jwt:Issuer", "https://api.pethost.test");
            builder.UseSetting("Jwt:Audience", "pethost-app");
            builder.UseSetting("Jwt:Key", "chave-de-teste-com-mais-de-32-bytes-para-hmac-sha256");
            builder.UseSetting("Jwt:AccessTokenLifetimeMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenLifetimeDays", "30");

            builder.UseSetting("Argon2:MemorySizeKib", "1024");
            builder.UseSetting("Argon2:Iterations", "1");
            builder.UseSetting("Argon2:DegreeOfParallelism", "1");

            // Os testes fazem dezenas de logins do mesmo "IP": o rate limit tem testes próprios.
            builder.UseSetting("RateLimiting:Enabled", "false");

            builder.UseSetting("Seed:Admin:Enabled", "true");
            builder.UseSetting("Seed:Admin:Email", AdminEmail);
            builder.UseSetting("Seed:Admin:Password", AdminPassword);

            builder.UseSetting("Email:Host", "smtp.invalid");
            builder.UseSetting("Email:FromAddress", "no-reply@pethost.test");

            foreach (var (key, value) in _bucket.Settings)
                builder.UseSetting(key, value);

            builder.ConfigureTestServices(services =>
                services.AddSingleton<IEmailSender, NoOpEmailSender>());
        });

        await Services.InitializeAuthModuleAsync(applyMigrations: true);
        await Services.InitializeOwnersModuleAsync(applyMigrations: true);
        await Services.InitializeAuditModuleAsync(applyMigrations: true);

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [AuthDbContext.Schema, OwnersDbContext.Schema, AuditDbContext.Schema],
            TablesToIgnore =
            [
                new Table(AuthDbContext.Schema, AuthDbContext.MigrationsHistoryTable),
                new Table(OwnersDbContext.Schema, OwnersDbContext.MigrationsHistoryTable),
                new Table(AuditDbContext.Schema, AuditDbContext.MigrationsHistoryTable),
            ],
        });
    }

    /// <summary>Limpa os schemas e recria o admin do seed.</summary>
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
        await _bucket.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class OwnersApiCollection : ICollectionFixture<OwnersApiFixture>
{
    public const string Name = "owners-api";
}
