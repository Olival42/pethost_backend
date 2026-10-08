using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PetHost.Api.Authentication;
using PetHost.Api.HealthChecks;
using PetHost.Modules.Auth.Application;
using PetHost.Modules.Auth.Infrastructure;
using PetHost.Modules.Auth.Infrastructure.Security;
using PetHost.Modules.Auth.Presentation;
using PetHost.Shared.Infrastructure.Email;
using PetHost.Shared.Infrastructure.Http;
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
    .AddAuthPresentation();

// O mesmo serializador vale para o que não passa pelo MVC — o handler global de
// exceção e o 401 do JWT escrevem com WriteAsJsonAsync, que usa estas opções.
builder.Services.ConfigureHttpJsonOptions(json => ConfigureJson(json.SerializerOptions));

builder.Services.AddOpenApi();

// --- TimeProvider injetado: nenhum DateTime.UtcNow no domínio (§9) ---
builder.Services.AddSingleton(TimeProvider.System);

// --- E-mail: fila em memória + envio SMTP em segundo plano ---
builder.Services.AddEmail(builder.Configuration);

// --- Módulo Auth ---
builder.Services.AddAuthApplication();
builder.Services.AddAuthInfrastructure(builder.Configuration);

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

// --- Health checks (§15) ---
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

// --- Exceção não tratada: um único ponto de captura (§15) ---
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness: o processo responde. Readiness: as dependências respondem.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

// Migrations no startup só fora de produção; em produção é step do pipeline (§11).
// O seed do admin é idempotente e roda sempre que estiver habilitado.
await app.Services.InitializeAuthModuleAsync(
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
