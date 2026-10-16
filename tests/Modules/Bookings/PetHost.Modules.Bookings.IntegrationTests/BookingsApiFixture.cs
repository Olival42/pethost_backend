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
using PetHost.Modules.Payments.Infrastructure;
using PetHost.Modules.Payments.Infrastructure.Persistence;
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

namespace PetHost.Modules.Bookings.IntegrationTests;

/// <summary>
/// Sobe Postgres, Redis e o bucket em container e a API em memória. A reserva cruza módulos
/// (conta no Auth, tutor no Owners, anfitrião no Hosts, pet no Pets, cantinho no Listings), então
/// a fixture aplica as migrations e limpa os schemas de todos. O mapa é de mentira e a tarefa de
/// expiração fica desligada (o teste chama o comando direto).
/// </summary>
public sealed class BookingsApiFixture : IAsyncLifetime
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

    public IServiceProvider Services => Factory.Services;

    /// <summary>Admin criado pelo seeder, para cancelar, encerrar a estadia e suspender.</summary>
    public const string AdminEmail = "admin@pethost.test";

    public const string AdminPassword = "senha-do-admin-123";

    /// <summary>E-mails que a API mandou (o despachante entrega aqui em vez do SMTP).</summary>
    public System.Collections.Concurrent.ConcurrentQueue<EmailMessage> SentEmails { get; } = new();

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
            builder.UseSetting("Payments:MaintenanceEnabled", "false");

            builder.UseSetting("Seed:Admin:Enabled", "true");
            builder.UseSetting("Seed:Admin:Email", AdminEmail);
            builder.UseSetting("Seed:Admin:Password", AdminPassword);

            builder.UseSetting("Email:Host", "smtp.invalid");
            builder.UseSetting("Email:FromAddress", "no-reply@pethost.test");

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
        await Services.InitializePaymentsModuleAsync(applyMigrations: true);

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();

        string[] schemas =
        [
            AuthDbContext.Schema, OwnersDbContext.Schema, AuditDbContext.Schema, HostsDbContext.Schema,
            PetsDbContext.Schema, ListingsDbContext.Schema, BookingsDbContext.Schema, PaymentsDbContext.Schema, EmailDbContext.Schema,
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
        // O despachante entrega em segundo plano: e-mail que o teste anterior deixou na fila chegaria
        // depois do Clear e cairia no teste seguinte (era o teste instável de e-mail). A linha só
        // sai da fila depois de entregue, então fila vazia = nada mais vai chegar.
        await WaitForEmailOutboxToDrainAsync(TimeSpan.FromSeconds(15));
        SentEmails.Clear();

        if (_respawner is not null && _connection is not null)
            await _respawner.ResetAsync(_connection);

        await Services.InitializeAuthModuleAsync(applyMigrations: false);
    }

    private async Task WaitForEmailOutboxToDrainAsync(TimeSpan timeout)
    {
        if (_connection is null)
            return;

        var deadline = DateTimeOffset.UtcNow + timeout;
        await using var pending = new NpgsqlCommand(
            $"SELECT count(*) FROM {EmailDbContext.Schema}.outbox_emails WHERE failed_at IS NULL",
            (NpgsqlConnection)_connection);

        while ((long)(await pending.ExecuteScalarAsync())! > 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(50);
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

    private sealed class RecordingEmailSender(System.Collections.Concurrent.ConcurrentQueue<EmailMessage> sent) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            sent.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>Todo endereço cai na Zona 7 de Maringá: a reserva não depende da distância.</summary>
    private sealed class MaringaGeocoder : IGeocoder
    {
        public Task<GeocodingResult> LocateAsync(ListingAddressData address, CancellationToken cancellationToken) =>
            Task.FromResult(GeocodingResult.Found(-23.410000m, -51.933000m));
    }
}

[CollectionDefinition(Name)]
public sealed class BookingsApiCollection : ICollectionFixture<BookingsApiFixture>
{
    public const string Name = "bookings-api";
}
