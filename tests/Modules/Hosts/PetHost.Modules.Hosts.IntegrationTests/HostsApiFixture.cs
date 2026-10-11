using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetHost.Modules.Audit.Infrastructure;
using PetHost.Modules.Audit.Infrastructure.Persistence;
using PetHost.Modules.Auth.Infrastructure;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.Modules.Hosts.Infrastructure;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Owners.Infrastructure;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.Shared.Infrastructure.Email;
using PetHost.TestKit;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace PetHost.Modules.Hosts.IntegrationTests;

/// <summary>
/// Sobe Postgres, Redis e o RustFS (buckets público e privado) em container e a API em memória.
/// A verificação do anfitrião cruza Auth (conta), Hosts e Audit, então a fixture aplica as
/// migrations e limpa os schemas dos três.
/// </summary>
public sealed class HostsApiFixture : IAsyncLifetime
{
    /// <summary>Admin criado pelo seeder, para a fila de análise.</summary>
    public const string AdminEmail = "admin@pethost.test";

    public const string AdminPassword = "senha-do-admin-123";

    /// <summary>Anfitrião de demonstração criado pelo seed (já verificado, com Stripe de teste).</summary>
    public const string DemoHostEmail = "demo-host@pethost.test";

    public const string DemoHostPassword = "Demo@Senha123";

    public const string DemoHostStripeAccount = "acct_1TestDemoHost";

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

    /// <summary>Os buckets (público e privado) que a API usa.</summary>
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

            builder.UseSetting("RateLimiting:Enabled", "false");

            builder.UseSetting("Seed:Admin:Enabled", "true");
            builder.UseSetting("Seed:Admin:Email", AdminEmail);
            builder.UseSetting("Seed:Admin:Password", AdminPassword);

            builder.UseSetting("Email:Host", "smtp.invalid");
            builder.UseSetting("Email:FromAddress", "no-reply@pethost.test");

            // A limpeza em segundo plano fica desligada: os testes chamam a rodada direto.
            builder.UseSetting("HostVerification:PurgeEnabled", "false");

            builder.UseSetting("Seed:DemoHost:Enabled", "true");
            builder.UseSetting("Seed:DemoHost:Email", DemoHostEmail);
            builder.UseSetting("Seed:DemoHost:Password", DemoHostPassword);
            builder.UseSetting("Seed:DemoHost:Cpf", "123.456.789-09");
            builder.UseSetting("Seed:DemoHost:StripeAccountId", DemoHostStripeAccount);

            foreach (var (key, value) in _bucket.Settings)
                builder.UseSetting(key, value);

            builder.ConfigureTestServices(services =>
                services.AddSingleton<IEmailSender, NoOpEmailSender>());
        });

        await Services.InitializeAuthModuleAsync(applyMigrations: true);
        await Services.InitializeAuditModuleAsync(applyMigrations: true);
        await Services.InitializeOwnersModuleAsync(applyMigrations: true);
        await Services.InitializeHostsModuleAsync(applyMigrations: true);

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [AuthDbContext.Schema, AuditDbContext.Schema, OwnersDbContext.Schema, HostsDbContext.Schema],
            TablesToIgnore =
            [
                new Table(AuthDbContext.Schema, AuthDbContext.MigrationsHistoryTable),
                new Table(AuditDbContext.Schema, AuditDbContext.MigrationsHistoryTable),
                new Table(OwnersDbContext.Schema, OwnersDbContext.MigrationsHistoryTable),
                new Table(HostsDbContext.Schema, HostsDbContext.MigrationsHistoryTable),
            ],
        });
    }

    /// <summary>Limpa os schemas e recria o admin e o anfitrião de demonstração do seed.</summary>
    public async Task ResetAsync()
    {
        if (_respawner is not null && _connection is not null)
            await _respawner.ResetAsync(_connection);

        await Services.InitializeAuthModuleAsync(applyMigrations: false);
        await Services.InitializeHostsModuleAsync(applyMigrations: false);
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
public sealed class HostsApiCollection : ICollectionFixture<HostsApiFixture>
{
    public const string Name = "hosts-api";
}
