using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using PetHost.Api.Authentication;
using PetHost.Api.HealthChecks;
using PetHost.Api.OpenApi;
using PetHost.Modules.Audit.Application;
using PetHost.Modules.Audit.Infrastructure;
using PetHost.Modules.Audit.Presentation;
using PetHost.Modules.Auth.Application;
using PetHost.Modules.Auth.Infrastructure;
using PetHost.Modules.Auth.Infrastructure.Security;
using PetHost.Modules.Auth.Presentation;
using PetHost.Modules.Owners.Application;
using PetHost.Modules.Owners.Infrastructure;
using PetHost.Modules.Owners.Presentation;
using PetHost.Modules.Hosts.Application;
using PetHost.Modules.Hosts.Infrastructure;
using PetHost.Modules.Hosts.Presentation;
using PetHost.Modules.Pets.Application;
using PetHost.Modules.Pets.Infrastructure;
using PetHost.Modules.Pets.Presentation;
using PetHost.Modules.Listings.Application;
using PetHost.Modules.Listings.Infrastructure;
using PetHost.Modules.Listings.Presentation;
using PetHost.Modules.Bookings.Application;
using PetHost.Modules.Bookings.Infrastructure;
using PetHost.Modules.Bookings.Presentation;
using PetHost.Modules.Conversations.Application;
using PetHost.Modules.Conversations.Infrastructure;
using PetHost.Modules.Conversations.Presentation;
using PetHost.Shared.Infrastructure.Email;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.Identity;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Infrastructure.Realtime;
using PetHost.Shared.Infrastructure.Storage;
using PetHost.Shared.Infrastructure.Stripe;
using Serilog;
using Serilog.Formatting.Json;

var builder = WebApplication.CreateBuilder(args);

// --- Logs: Serilog em JSON estruturado (§15) ---
builder.Services.AddSerilog(logger => logger
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new JsonFormatter()));

// --- MVC + contrato de resposta (§7) ---
builder.Services
    .AddControllers(mvc =>
    {
        mvc.SuppressAsyncSuffixInActionNames = false;

        // JSON malformado ou corpo vazio: 400 no envelope, em vez de chegar
        // ao handler como null e virar 500.
        mvc.Filters.Add<MalformedRequestBodyFilter>();
    })
    .ConfigureApiBehaviorOptions(behavior =>
    {
        behavior.SuppressModelStateInvalidFilter = true;   // a validação é nossa
        behavior.SuppressMapClientErrors = true;

        // Upload de imagem: o [ImageUploadEndpoint] confere o content-type e responde o 415
        // dizendo o formato certo. Sem isto, o MVC barraria antes, com um 415 genérico.
        behavior.SuppressConsumesConstraintForFormFileParameters = true;
    })
    .AddJsonOptions(json => ConfigureJson(json.JsonSerializerOptions))
    .AddAuthPresentation()
    .AddOwnersPresentation()
    .AddHostsPresentation()
    .AddPetsPresentation()
    .AddListingsPresentation()
    .AddBookingsPresentation()
    .AddConversationsPresentation()
    .AddAuditPresentation()
    .AddStripeWebhooks();

// O mesmo serializador vale para o que não passa pelo MVC — o handler global de
// exceção e o 401 do JWT escrevem com WriteAsJsonAsync, que usa estas opções.
builder.Services.ConfigureHttpJsonOptions(json => ConfigureJson(json.SerializerOptions));

// --- Tempo real (SignalR): o hub só empurra eventos, com o mesmo JSON da API ---
builder.Services.AddRealtime(ConfigureJson);

// Documento OpenAPI (/openapi/v1.json) com o JWT declarado, para o Swagger UI.
builder.Services.AddOpenApi(openApi => openApi.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

// --- TimeProvider injetado: nenhum DateTime.UtcNow no domínio (§9) ---
builder.Services.AddSingleton(TimeProvider.System);

// --- E-mail: caixa de saída no Postgres + envio SMTP em segundo plano (com novas tentativas) ---
builder.Services.AddEmail(builder.Configuration);

// --- Imagens: bucket S3 compatível (R2 em produção, RustFS em desenvolvimento) ---
builder.Services.AddImageStorage(builder.Configuration);

// --- Stripe (modo de teste): cliente da API e o endpoint único de webhooks.
//     Cada módulo registra o que trata (o Hosts: account.updated das contas conectadas). ---
builder.Services.AddStripe(builder.Configuration);

// --- CPF identifica a pessoa: a mesma pessoa (e-mail) em tutor e anfitrião (restrição 9) ---
builder.Services.AddCpfOwnership();

// --- Módulo Audit (trilha de auditoria, usada pelos outros módulos) ---
builder.Services.AddAuditApplication();
builder.Services.AddAuditInfrastructure(builder.Configuration);

// --- Módulo Auth ---
builder.Services.AddAuthApplication();
builder.Services.AddAuthInfrastructure(builder.Configuration);

// --- Módulo Owners (tutores) ---
builder.Services.AddOwnersApplication();
builder.Services.AddOwnersInfrastructure(builder.Configuration);

// --- Módulo Hosts (anfitriões): verificação de identidade aprovada pelo admin ---
builder.Services.AddHostsApplication();
builder.Services.AddHostsInfrastructure(builder.Configuration);

// --- Módulo Pets ---
builder.Services.AddPetsApplication();
builder.Services.AddPetsInfrastructure(builder.Configuration);

// --- Módulo Listings (cantinhos): cadastro, fotos e busca por raio (geocodificação no Nominatim) ---
builder.Services.AddListingsApplication();
builder.Services.AddListingsInfrastructure(builder.Configuration);

// --- Módulo Bookings (reservas): pedido, aceite/recusa, cancelamento, check-in e check-out ---
builder.Services.AddBookingsApplication();
builder.Services.AddBookingsInfrastructure(builder.Configuration);

// --- Módulo Conversations (chat): conversa por dupla tutor–anfitrião, texto e foto, hub em tempo real ---
builder.Services.AddConversationsApplication();
builder.Services.AddConversationsInfrastructure(builder.Configuration);

// --- Autenticação ---
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{JwtOptions.SectionName}' is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwt =>
    {
        // Mantém as claims com o nome que o token traz ("sub", "role") em vez de
        // trocá-las pelas URIs longas do WS-Security.
        jwt.MapInboundClaims = false;
        jwt.Events = JwtEnvelopeEvents.Create();

        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,

            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),

            NameClaimType = JwtClaimNames.Name,
            RoleClaimType = JwtClaimNames.Role,
        };
    });

builder.Services.AddAuthorization();

// --- Rate limit: geral para todo endpoint, mais rígido nos críticos ---
builder.Services.AddPetHostRateLimiting(builder.Configuration);

// --- IP real atrás de proxy (ngrok, balanceador) ---
// O rate limit por IP depende disso: sem ler o X-Forwarded-For, todo cliente que chega
// pelo proxy teria o IP do proxy e cairia no mesmo limite. Só proxies de rede privada
// (a rede do Docker) e loopback são confiáveis — senão qualquer um forjaria o header.
var trustForwardedHeaders = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarded.ForwardLimit = 1;
        forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
        forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
        forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
        forwarded.KnownProxies.Add(IPAddress.IPv6Loopback);
    });
}

// --- Health checks (§15) ---
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

// --- Exceção não tratada: um único ponto de captura (§15) ---
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (trustForwardedHeaders)
    app.UseForwardedHeaders();

app.UseExceptionHandler();

// Erro sem corpo (rota inexistente, id fora do formato, método errado) sai no envelope.
app.UseEmptyErrorEnvelope();
app.UseSerilogRequestLogging();

// Só fora de produção: o documento lista todas as rotas e o formato de cada uma.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI em /swagger, lendo o documento gerado acima.
    app.UseSwaggerUI(swagger =>
    {
        swagger.SwaggerEndpoint("/openapi/v1.json", "PetHost API v1");
        swagger.DocumentTitle = "PetHost API";
        swagger.EnablePersistAuthorization();
        swagger.DisplayRequestDuration();
    });
}

app.UseAuthentication();

// Depois da autenticação: endpoint logado é limitado pelo usuário do token, não pelo IP.
app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();
app.MapConversationsHub();

// Liveness: o processo responde. Readiness: as dependências respondem.
// Fora do rate limit: o healthcheck do Docker e o balanceador chamam toda hora.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).DisableRateLimiting();

// Migrations no startup só fora de produção; em produção é step do pipeline (§11).
// O seed do admin é idempotente e roda sempre que estiver habilitado.
await app.Services.InitializeEmailAsync(
    applyMigrations: app.Environment.IsDevelopment());

await app.Services.InitializeAuthModuleAsync(
    applyMigrations: app.Environment.IsDevelopment());

await app.Services.InitializeOwnersModuleAsync(
    applyMigrations: app.Environment.IsDevelopment());

await app.Services.InitializeAuditModuleAsync(
    applyMigrations: app.Environment.IsDevelopment());

await app.Services.InitializeHostsModuleAsync(
    applyMigrations: app.Environment.IsDevelopment());

await app.Services.InitializePetsModuleAsync(
    applyMigrations: app.Environment.IsDevelopment());

await app.Services.InitializeListingsModuleAsync(
    applyMigrations: app.Environment.IsDevelopment());

await app.Services.InitializeBookingsModuleAsync(
    applyMigrations: app.Environment.IsDevelopment());

await app.Services.InitializeConversationsModuleAsync(
    applyMigrations: app.Environment.IsDevelopment());

await app.RunAsync();

static void ConfigureJson(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
}

/// <summary>
/// Declarada explicitamente para que a <c>WebApplicationFactory&lt;Program&gt;</c>
/// dos testes de integração consiga referenciar o ponto de entrada, que as
/// top-level statements geram como <c>internal</c>.
/// </summary>
public partial class Program;
