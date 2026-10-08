using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace PetHost.Api.HealthChecks;

/// <summary>
/// Readiness do Redis. Entra no <c>ready</c> porque o login depende dele: sem
/// Redis não há como emitir refresh token.
/// </summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer connectionMultiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await connectionMultiplexer.GetDatabase().PingAsync().ConfigureAwait(false);

            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Redis is not reachable.", exception);
        }
    }
}
