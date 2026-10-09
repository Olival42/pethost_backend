using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetHost.Modules.Audit.Infrastructure;
using PetHost.Modules.Audit.Infrastructure.Persistence;
using PetHost.Modules.Auth.Infrastructure;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.Shared.Infrastructure.Email;
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

    /// <summary>E-mails que a API "mandou". Limpo a cada <see cref="ResetAsync"/>.</summary>
    public CapturingEmailSender Emails { get; } = new();

    public HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>
    /// Outra instância da API sobre os mesmos containers, com configuração extra — por
    /// exemplo, o rate limit ligado com limites baixos. Quem chama descarta.
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

            // Os testes fazem dezenas de logins do mesmo "IP": o rate limit tem testes próprios.
            builder.UseSetting("RateLimiting:Enabled", "false");

            builder.UseSetting("Seed:Admin:Enabled", "true");
            builder.UseSetting("Seed:Admin:Email", SeededAdminEmail);
            builder.UseSetting("Seed:Admin:Password", SeededAdminPassword);
            builder.UseSetting("Seed:Admin:FullName", "PetHost Admin");

            // O host SMTP só precisa passar na validação de subida: o envio é
            // trocado pelo coletor abaixo e nunca abre conexão.
            builder.UseSetting("Email:Host", "smtp.invalid");
            builder.UseSetting("Email:Port", "25");
            builder.UseSetting("Email:FromAddress", "no-reply@pethost.test");
            builder.UseSetting("PasswordReset:TokenLifetimeMinutes", "30");
            builder.UseSetting("PasswordReset:ResetUrl", "https://app.pethost.test/redefinir-senha?token={token}");

            builder.ConfigureTestServices(services =>
                services.AddSingleton<IEmailSender>(Emails));
        });

        // A WebApplicationFactory para o host no `builder.Build()`, então o
        // trecho final do Program.cs (migrations + seed) não roda sozinho aqui.
        await Services.InitializeAuthModuleAsync(applyMigrations: true);
        await Services.InitializeAuditModuleAsync(applyMigrations: true);

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [AuthDbContext.Schema, AuditDbContext.Schema],
            TablesToIgnore =
            [
                new Respawn.Graph.Table(AuthDbContext.Schema, AuthDbContext.MigrationsHistoryTable),
                new Respawn.Graph.Table(AuditDbContext.Schema, AuditDbContext.MigrationsHistoryTable),
            ],
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

        Emails.Clear();
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
