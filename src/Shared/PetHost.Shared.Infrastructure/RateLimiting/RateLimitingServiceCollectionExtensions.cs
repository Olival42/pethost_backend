using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Shared.Infrastructure.RateLimiting;

/// <summary>
/// Rate limit da API: um limite geral para todo endpoint e políticas mais rígidas para
/// os críticos (<see cref="RateLimitPolicies"/>). Estourou: <c>429 TOO_MANY_REQUESTS</c>
/// no envelope, com <c>Retry-After</c>.
/// </summary>
/// <remarks>
/// Os contadores ficam na memória do processo — certo para uma instância só. Com mais
/// de uma instância atrás de um balanceador, o limite passa a valer por instância.
/// <para>
/// Endpoint anônimo é limitado por IP; endpoint logado, pelo usuário do token (<c>sub</c>),
/// para que uma rede compartilhada (escritório, 4G) não derrube quem está logado. Atrás
/// de proxy (ngrok), o IP real depende do <c>UseForwardedHeaders</c> no host.
/// </para>
/// </remarks>
public static class RateLimitingServiceCollectionExtensions
{
    private static readonly HashSet<string> PerUserPolicies =
        [RateLimitPolicies.AccountSensitive, RateLimitPolicies.AccountUpdate];

    public static IServiceCollection AddPetHostRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RateLimitingOptions();
        configuration.GetSection(RateLimitingOptions.SectionName).Bind(options);
        Validate(options);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                Partition(options.Enabled, UserOrClientKey(context), options.Global));

            // Desligado, as políticas continuam registradas (sem limite): os atributos
            // [EnableRateLimiting] dos controllers exigem que o nome exista.
            foreach (var name in RateLimitPolicies.All)
            {
                var rule = options.Policies[name];
                var perUser = PerUserPolicies.Contains(name);

                limiter.AddPolicy(name, context =>
                    Partition(options.Enabled, perUser ? UserOrClientKey(context) : ClientKey(context), rule));
            }
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(bool enabled, string key, RateLimitRule rule) =>
        enabled
            ? RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rule.PermitLimit,
                Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            })
            : RateLimitPartition.GetNoLimiter(key);

    private static string ClientKey(HttpContext context) =>
        "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static string UserOrClientKey(HttpContext context) =>
        context.User.FindFirst("sub")?.Value is { Length: > 0 } userId ? "user:" + userId : ClientKey(context);

    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? (int)Math.Ceiling(wait.TotalSeconds)
            : (int?)null;

        if (retryAfter is { } seconds)
            context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RateLimitingServiceCollectionExtensions))
            .LogRateLimited(context.Request.Path, context.Connection.RemoteIpAddress?.ToString());

        var message = retryAfter is { } s
            ? $"Too many requests. Try again in {s} seconds."
            : "Too many requests. Try again later.";

        await context.Response
            .WriteAsJsonAsync(ApiResponse<Unit>.Fail(new ErrorResponse(ErrorCodes.TooManyRequests, message)), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Validate(RateLimitingOptions options)
    {
        if (!options.Enabled)
            return;

        foreach (var (name, rule) in options.Policies.Append(new("global", options.Global)))
        {
            if (rule.PermitLimit <= 0 || rule.WindowSeconds <= 0)
            {
                throw new InvalidOperationException(
                    $"{RateLimitingOptions.SectionName}: '{name}' needs PermitLimit and WindowSeconds greater than zero.");
            }
        }
    }
}

internal static partial class RateLimitingLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit exceeded on {Path} from {ClientIp}")]
    public static partial void LogRateLimited(this ILogger logger, PathString path, string? clientIp);
}
