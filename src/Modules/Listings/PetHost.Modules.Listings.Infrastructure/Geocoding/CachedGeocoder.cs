using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;
using StackExchange.Redis;

namespace PetHost.Modules.Listings.Infrastructure.Geocoding;

/// <summary>
/// Cache no <b>Redis</b> na frente do geocodificador: vale para todas as instâncias da API, e a
/// busca pelo endereço da conta consulta o mapa uma vez por endereço, não a cada busca. "Achou"
/// fica <see cref="GeocodingOptions.CacheHours"/>; "não achou", bem menos; "fora do ar" não fica.
/// </summary>
/// <remarks>
/// Chave <c>pethost:listing:geocoding:&lt;sha256&gt;</c> — o hash do endereço normalizado, para
/// não guardar endereço de ninguém em texto no Redis. Valor <c>found|lat|lng</c> ou
/// <c>not-found</c>; a expiração é o TTL da chave. Redis fora do ar não derruba o pedido: a
/// consulta vai direto ao mapa (com log).
/// </remarks>
internal sealed partial class CachedGeocoder(
    IGeocoder inner,
    IConnectionMultiplexer redis,
    IOptions<GeocodingOptions> options,
    ILogger<CachedGeocoder> logger) : IGeocoder
{
    private const string KeyPrefix = "pethost:listing:geocoding:";
    private const string NotFoundValue = "not-found";
    private const string FoundPrefix = "found|";

    public async Task<GeocodingResult> LocateAsync(ListingAddressData address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        var key = KeyFor(address);

        var cached = await ReadAsync(key).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var result = await inner.LocateAsync(address, cancellationToken).ConfigureAwait(false);

        await WriteAsync(key, result).ConfigureAwait(false);

        return result;
    }

    /// <summary>O mesmo endereço escrito com outra caixa ou espaços cai na mesma chave.</summary>
    internal static RedisKey KeyFor(ListingAddressData address)
    {
        var normalized = string.Join(
            "|",
            new[] { address.ZipCode, address.Street, address.Number, address.Neighborhood, address.City, address.State }
                .Select(part => part?.Trim().ToUpperInvariant() ?? string.Empty));

        return KeyPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private async Task<GeocodingResult?> ReadAsync(RedisKey key)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(key).ConfigureAwait(false);

            return value.IsNullOrEmpty ? null : Parse(value.ToString());
        }
        catch (RedisException exception)
        {
            LogCacheUnavailable(exception);
            return null;
        }
    }

    private async Task WriteAsync(RedisKey key, GeocodingResult result)
    {
        var (value, lifetime) = result.Status switch
        {
            GeocodingStatus.Found => (Format(result), TimeSpan.FromHours(options.Value.CacheHours)),
            GeocodingStatus.NotFound => (NotFoundValue, TimeSpan.FromMinutes(options.Value.NotFoundCacheMinutes)),
            _ => (null, TimeSpan.Zero),
        };

        if (value is null)
            return;

        try
        {
            await redis.GetDatabase().StringSetAsync(key, value, lifetime).ConfigureAwait(false);
        }
        catch (RedisException exception)
        {
            LogCacheUnavailable(exception);
        }
    }

    private static string Format(GeocodingResult result) =>
        string.Create(CultureInfo.InvariantCulture, $"{FoundPrefix}{result.Latitude}|{result.Longitude}");

    /// <summary>Valor ilegível (formato antigo, corrompido) é tratado como ausente.</summary>
    private static GeocodingResult? Parse(string value)
    {
        if (value == NotFoundValue)
            return GeocodingResult.NotFound;

        var parts = value.Split('|');

        return parts is [_, var lat, var lng]
            && value.StartsWith(FoundPrefix, StringComparison.Ordinal)
            && decimal.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && decimal.TryParse(lng, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
                ? GeocodingResult.Found(latitude, longitude)
                : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geocoding cache (Redis) is unavailable; calling the map service directly.")]
    private partial void LogCacheUnavailable(Exception exception);
}
