using Microsoft.Extensions.Options;

namespace PetHost.Modules.Listings.Infrastructure.Geocoding;

/// <summary>
/// Fila única para o serviço de mapa: um pedido por vez, com o intervalo mínimo da política do
/// Nominatim entre eles. Singleton — vale para a instância inteira da API.
/// </summary>
internal sealed class GeocodingRateGate(IOptions<GeocodingOptions> options, TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private DateTimeOffset _lastCallAt = DateTimeOffset.MinValue;

    /// <summary>Roda <paramref name="call"/> quando for a vez dele.</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var interval = TimeSpan.FromMilliseconds(options.Value.MinIntervalMilliseconds);
            var wait = _lastCallAt + interval - timeProvider.GetUtcNow();
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);

            try
            {
                return await call(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lastCallAt = timeProvider.GetUtcNow();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
