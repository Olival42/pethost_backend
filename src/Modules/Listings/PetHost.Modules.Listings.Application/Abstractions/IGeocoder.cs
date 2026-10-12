using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Abstractions;

/// <summary>
/// Porta do geocodificador: transforma um endereço em coordenada. A implementação consulta um
/// serviço de mapa externo (Nominatim/OpenStreetMap) e guarda o resultado em cache.
/// </summary>
public interface IGeocoder
{
    /// <summary>
    /// A coordenada do endereço. Nunca lança por falha do serviço: devolve
    /// <see cref="GeocodingStatus.Unavailable"/>.
    /// </summary>
    Task<GeocodingResult> LocateAsync(ListingAddressData address, CancellationToken cancellationToken);
}

/// <summary>Como terminou a consulta ao mapa.</summary>
public enum GeocodingStatus
{
    /// <summary>Achou: a coordenada está no resultado.</summary>
    Found,

    /// <summary>O serviço respondeu, mas não achou o endereço.</summary>
    NotFound,

    /// <summary>O serviço não respondeu (fora do ar, lento, recusou).</summary>
    Unavailable,
}

/// <summary>Resultado da consulta ao mapa. Latitude e longitude só valem em <see cref="GeocodingStatus.Found"/>.</summary>
public sealed record GeocodingResult(GeocodingStatus Status, decimal Latitude = 0, decimal Longitude = 0)
{
    public static GeocodingResult Found(decimal latitude, decimal longitude) => new(GeocodingStatus.Found, latitude, longitude);

    public static GeocodingResult NotFound { get; } = new(GeocodingStatus.NotFound);

    public static GeocodingResult Unavailable { get; } = new(GeocodingStatus.Unavailable);
}
