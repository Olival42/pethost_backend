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
using PetHost.Modules.Hosts.Infrastructure;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Infrastructure;
using PetHost.Modules.Listings.Infrastructure.Persistence;
using PetHost.Modules.Owners.Infrastructure;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.Modules.Payments.Application.Abstractions;
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

namespace PetHost.Modules.Payments.IntegrationTests;

/// <summary>
/// Sobe Postgres, Redis e o bucket em container e a API em memória, com o Stripe em memória
/// (<see cref="FakeStripePayments"/>) e o segredo de webhook de teste: o pagamento cruza todos os
/// módulos (conta, tutor, anfitrião, pet, cantinho, reserva), então a fixture aplica as migrations
/// e limpa os schemas de todos. Tarefas em segundo plano desligadas (o teste chama os comandos).
/// </summary>
public sealed class PaymentsApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("pethost")
        .WithUsername("pethost")
        .WithPassword("pethost")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly TestImageBucket _bucket = new();

    public const string AdminEmail = "admin@pethost.test";

    public const string AdminPassword = "senha-do-admin-123";

    private WebApplicationFactory<Program>? _factory;
    private Respawner? _respawner;
    private DbConnection? _connection;

    public HttpClient CreateClient() => Factory.CreateClient();

    public IServiceProvider Services => Factory.Services;

    /// <summary>O Stripe em memória que a API usa.</summary>
    public FakeStripePayments Stripe { get; } = new();

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
            builder.UseSetting("HostVerification:CpfFingerprintKey", "chave-de-teste-da-impressao-do-cpf-hmac-sha256");
            builder.UseSetting("Jwt:AccessTokenLifetimeMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenLifetimeDays", "30");

            builder.UseSetting("Argon2:MemorySizeKib", "1024");
            builder.UseSetting("Argon2:Iterations", "1");
            builder.UseSetting("Argon2:DegreeOfParallelism", "1");

            builder.UseSetting("RateLimiting:Enabled", "false");
            builder.UseSetting("HostVerification:PurgeEnabled", "false");
            builder.UseSetting("HostPayouts:MaintenanceEnabled", "false");
            builder.UseSetting("BookingExpiration:Enabled", "false");
            builder.UseSetting("Payments:MaintenanceEnabled", "false");
            builder.UseSetting("Payments:AdminAlertEmail", "admin@pethost.test");

            builder.UseSetting("Stripe:WebhookSecret", StripeWebhookSigner.Secret);

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
                services.AddSingleton<IStripePayments>(Stripe);
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

    /// <summary>Limpa os schemas, o Stripe em memória e os e-mails, e recria o admin do seed.</summary>
    public async Task ResetAsync()
    {
        SentEmails.Clear();
        Stripe.Reset();

        if (_respawner is not null && _connection is not null)
            await _respawner.ResetAsync(_connection);

        await Services.InitializeAuthModuleAsync(applyMigrations: false);
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

    /// <summary>Todo endereço cai na Zona 7 de Maringá.</summary>
    private sealed class MaringaGeocoder : IGeocoder
    {
        public Task<GeocodingResult> LocateAsync(ListingAddressData address, CancellationToken cancellationToken) =>
            Task.FromResult(GeocodingResult.Found(-23.410000m, -51.933000m));
    }
}

[CollectionDefinition(Name)]
public sealed class PaymentsApiCollection : ICollectionFixture<PaymentsApiFixture>
{
    public const string Name = "payments-api";
}
