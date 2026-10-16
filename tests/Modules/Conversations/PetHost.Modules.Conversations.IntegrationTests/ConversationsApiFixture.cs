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
using PetHost.Modules.Payments.Infrastructure;
using PetHost.Modules.Payments.Infrastructure.Persistence;
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
    /// <summary>Admin criado pelo seeder, para a fila de denúncias.</summary>
    public const string AdminEmail = "admin@pethost.test";

    /// <summary>Senha do admin do seeder (só de teste).</summary>
    public const string AdminPassword = "senha-do-admin-123";

    /// <summary>Quem recebe o aviso de denúncia nova.</summary>
    public const string ModerationAlertEmail = "moderacao@pethost.test";

    /// <summary>Os e-mails que a caixa de saída entregou (o SMTP é trocado por este coletor).</summary>
    public System.Collections.Concurrent.ConcurrentQueue<EmailMessage> SentEmails { get; } = new();

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
            builder.UseSetting("ConversationUnreadEmail:Enabled", "false");
            builder.UseSetting("Payments:MaintenanceEnabled", "false");

            builder.UseSetting("Email:Host", "smtp.invalid");
            builder.UseSetting("Email:FromAddress", "no-reply@pethost.test");

            builder.UseSetting("ConversationModeration:AdminAlertEmail", ModerationAlertEmail);

            builder.UseSetting("Seed:Admin:Enabled", "true");
            builder.UseSetting("Seed:Admin:Email", AdminEmail);
            builder.UseSetting("Seed:Admin:Password", AdminPassword);

            foreach (var (key, value) in _bucket.Settings)
                builder.UseSetting(key, value);

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IEmailSender>(new RecordingEmailSender(SentEmails));
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
        await Services.InitializePaymentsModuleAsync(applyMigrations: true);

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();

        string[] schemas =
        [
            AuthDbContext.Schema, OwnersDbContext.Schema, AuditDbContext.Schema, HostsDbContext.Schema, PetsDbContext.Schema,
            ListingsDbContext.Schema, BookingsDbContext.Schema, ConversationsDbContext.Schema, PaymentsDbContext.Schema, EmailDbContext.Schema,
        ];

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = schemas,
            TablesToIgnore = [.. schemas.Select(schema => new Table(schema, "__ef_migrations_history"))],
        });
    }

    /// <summary>Limpa os schemas e recria o admin do seed.</summary>
    public async Task ResetAsync()
    {
        SentEmails.Clear();

        if (_respawner is not null && _connection is not null)
            await _respawner.ResetAsync(_connection);

        await Services.InitializeAuthModuleAsync(applyMigrations: false);
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

    /// <summary>Não envia nada: guarda o que seria enviado, para o teste conferir (o aviso ao admin).</summary>
    private sealed class RecordingEmailSender(System.Collections.Concurrent.ConcurrentQueue<EmailMessage> sent) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            sent.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>Espera até a caixa de saída entregar um e-mail que satisfaça <paramref name="match"/>.</summary>
    public async Task<EmailMessage?> WaitForEmailAsync(Func<EmailMessage, bool> match, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var found = SentEmails.FirstOrDefault(match);
            if (found is not null)
                return found;

            await Task.Delay(100);
        }

        return null;
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
