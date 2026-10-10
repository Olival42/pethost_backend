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
using PetHost.Modules.Hosts.Infrastructure;
using PetHost.Modules.Pets.Application;
using PetHost.Modules.Pets.Infrastructure;
using PetHost.Modules.Pets.Presentation;
using PetHost.Shared.Infrastructure.Email;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
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
    })
    .AddJsonOptions(json => ConfigureJson(json.JsonSerializerOptions))
    .AddAuthPresentation()
    .AddOwnersPresentation()
    .AddPetsPresentation()
    .AddAuditPresentation();

// O mesmo serializador vale para o que não passa pelo MVC — o handler global de
// exceção e o 401 do JWT escrevem com WriteAsJsonAsync, que usa estas opções.
builder.Services.ConfigureHttpJsonOptions(json => ConfigureJson(json.SerializerOptions));

// Documento OpenAPI (/openapi/v1.json) com o JWT declarado, para o Swagger UI.
builder.Services.AddOpenApi(openApi => openApi.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

// --- TimeProvider injetado: nenhum DateTime.UtcNow no domínio (§9) ---
builder.Services.AddSingleton(TimeProvider.System);

// --- E-mail: fila em memória + envio SMTP em segundo plano ---
builder.Services.AddEmail(builder.Configuration);

// --- Módulo Audit (trilha de auditoria, usada pelos outros módulos) ---
builder.Services.AddAuditApplication();
builder.Services.AddAuditInfrastructure(builder.Configuration);

// --- Módulo Auth ---
builder.Services.AddAuthApplication();
builder.Services.AddAuthInfrastructure(builder.Configuration);

// --- Módulo Owners (tutores) ---
builder.Services.AddOwnersApplication();
builder.Services.AddOwnersInfrastructure(builder.Configuration);

// --- Módulo Hosts (anfitriões): por enquanto só a tabela e o contrato de leitura ---
builder.Services.AddHostsInfrastructure(builder.Configuration);

// --- Módulo Pets ---
builder.Services.AddPetsApplication();
builder.Services.AddPetsInfrastructure(builder.Configuration);

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

// Liveness: o processo responde. Readiness: as dependências respondem.
// Fora do rate limit: o healthcheck do Docker e o balanceador chamam toda hora.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).DisableRateLimiting();

// Migrations no startup só fora de produção; em produção é step do pipeline (§11).
// O seed do admin é idempotente e roda sempre que estiver habilitado.
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
