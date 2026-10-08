using Microsoft.Extensions.Diagnostics.HealthChecks;
using PetHost.Modules.Auth.Infrastructure.Persistence;

namespace PetHost.Api.HealthChecks;

/// <summary>Readiness do Postgres (§15): só responde saudável se o banco aceita conexão.</summary>
internal sealed class PostgresHealthCheck(AuthDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL refused the connection.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A mensagem da exceção fica no detalhe do health check, que é interno;
            // nunca chega ao envelope de erro da API.
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", exception);
        }
    }
}
