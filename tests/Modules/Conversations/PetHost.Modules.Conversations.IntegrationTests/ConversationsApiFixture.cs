using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetHost.Modules.Audit.Infrastructure;
using PetHost.Modules.Audit.Infrastructure.Persistence;
using PetHost.Modules.Auth.Infrastructure;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.Modules.Bookings.Infrastructure;
using PetHost.Modules.Bookings.Infrastructure.Persistence;
using PetHost.Modules.Conversations.Infrastructure;
using PetHost.Modules.Conversations.Infrastructure.Persistence;
using PetHost.Modules.Hosts.Infrastructure;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Infrastructure;
using PetHost.Modules.Listings.Infrastructure.Persistence;
using PetHost.Modules.Owners.Infrastructure;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.Modules.Pets.Infrastructure;
using PetHost.Modules.Pets.Infrastructure.Persistence;
using PetHost.Shared.Infrastructure.Email;
using PetHost.Shared.Infrastructure.Email.Persistence;
using PetHost.TestKit;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace PetHost.Modules.Conversations.IntegrationTests;

/// <summary>
/// Sobe Postgres, Redis e o bucket em container e a API em memória. A conversa cruza módulos
/// (conta no Auth, tutor no Owners, anfitrião no Hosts, cantinho no Listings, reserva no Bookings),
/// então a fixture aplica as migrations e limpa os schemas de todos. O mapa é de mentira e as
/// tarefas de fundo ficam desligadas (o teste chama o comando direto).
/// </summary>
public sealed class ConversationsApiFixture : IAsyncLifetime
{
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

    /// <summary>
    /// Outra instância da API sobre os mesmos containers, com configurações trocadas (ex.: rate
    /// limit ligado). Quem chama descarta.
    /// </summary>
    public WebApplicationFactory<Program> WithSettings(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });
    }

    public IServiceProvider Services => Factory.Services;

    /// <summary>O servidor em memória, para o cliente do SignalR (HTTP e WebSocket).</summary>
    public TestServer Server => Factory.Server;

    /// <summary>O bucket de imagens, para conferir a foto do chat lá e depois da retenção.</summary>
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
            builder.UseSetting("HostVerification:PurgeEnabled", "false");
            builder.UseSetting("BookingExpiration:Enabled", "false");
            builder.UseSetting("ConversationRetention:Enabled", "false");

            builder.UseSetting("Email:Host", "smtp.invalid");
            builder.UseSetting("Email:FromAddress", "no-reply@pethost.test");

            foreach (var (key, value) in _bucket.Settings)
                builder.UseSetting(key, value);

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IEmailSender, DiscardingEmailSender>();
                services.AddSingleton<IGeocoder, MaringaGeocoder>();
            });
        });

        await Services.InitializeEmailAsync(applyMigrations: true);
        await Services.InitializeAuthModuleAsync(applyMigrations: true);
        await Services.InitializeOwnersModuleAsync(applyMigrations: true);
        await Services.InitializeAuditModuleAsync(applyMigrations: true);
        await Services.InitializeHostsModuleAsync(applyMigrations: true);
        await Services.InitializePetsModuleAsync(applyMigrations: true);
        await Services.InitializeListingsModuleAsync(applyMigrations: true);
        await Services.InitializeBookingsModuleAsync(applyMigrations: true);
        await Services.InitializeConversationsModuleAsync(applyMigrations: true);

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();

        string[] schemas =
        [
            AuthDbContext.Schema, OwnersDbContext.Schema, AuditDbContext.Schema, HostsDbContext.Schema, PetsDbContext.Schema,
            ListingsDbContext.Schema, BookingsDbContext.Schema, ConversationsDbContext.Schema, EmailDbContext.Schema,
        ];

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = schemas,
            TablesToIgnore = [.. schemas.Select(schema => new Table(schema, "__ef_migrations_history"))],
        });
    }

    public async Task ResetAsync()
    {
        if (_respawner is not null && _connection is not null)
            await _respawner.ResetAsync(_connection);
    }

    /// <summary>
    /// Espera até alguma conexão ficar parada esperando uma trava <c>pg_advisory_xact_lock</c> (o
    /// <c>pg_locks</c> mostra a trava pedida e ainda não concedida). Devolve se aconteceu no prazo.
    /// </summary>
    public async Task<bool> WaitForAdvisoryLockWaiterAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted",
            (NpgsqlConnection)_connection!);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((long)(await command.ExecuteScalarAsync())! > 0)
                return true;

            await Task.Delay(50);
        }

        return false;
    }

    /// <summary>Roda SQL direto no banco (para simular o tempo passando).</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)_connection!);
        await command.ExecuteNonQueryAsync();
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

    /// <summary>Os e-mails da reserva não importam aqui: são descartados.</summary>
    private sealed class DiscardingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Todo endereço cai na Zona 7 de Maringá.</summary>
    private sealed class MaringaGeocoder : IGeocoder
    {
        public Task<GeocodingResult> LocateAsync(ListingAddressData address, CancellationToken cancellationToken) =>
            Task.FromResult(GeocodingResult.Found(-23.410000m, -51.933000m));
    }
}

[CollectionDefinition(Name)]
public sealed class ConversationsApiCollection : ICollectionFixture<ConversationsApiFixture>
{
    public const string Name = "conversations-api";
}
