using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings;

/// <summary>Endereço → coordenada, traduzindo o resultado do geocodificador para os erros do módulo.</summary>
public static class ListingLocator
{
    /// <summary>
    /// A coordenada do endereço. Não achou: <paramref name="notLocated"/>. Serviço fora do ar:
    /// <c>503 LISTING_GEOCODER_UNAVAILABLE</c>.
    /// </summary>
    public static async Task<Result<GeoPoint>> LocateAsync(
        IGeocoder geocoder,
        ListingAddressData address,
        Error notLocated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(geocoder);

        var result = await geocoder.LocateAsync(address, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            GeocodingStatus.Found => GeoPoint.Create(result.Latitude, result.Longitude),
            GeocodingStatus.NotFound => Result<GeoPoint>.Failure(notLocated),
            _ => Result<GeoPoint>.Failure(ListingsErrors.GeocoderUnavailable),
        };
    }
}
