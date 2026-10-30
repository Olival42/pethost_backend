using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// Um ponto no mapa (graus decimais, WGS 84), com 6 casas — cerca de 10 cm, mais que o
/// bastante para "2 km de você". Vem do geocodificador, nunca do anfitrião.
/// </summary>
/// <remarks>
/// A coordenada do cantinho <b>não sai</b> para quem não é o dono (escopo, regra 7). Para o mapa,
/// os outros recebem um ponto aproximado (<see cref="Approximate"/>): deslocado ao acaso e
/// arredondado, com um círculo de <see cref="ApproximateRadiusMeters"/> m em volta, como o Airbnb.
/// </remarks>
public sealed class GeoPoint : ValueObject
{
    /// <summary>Casas decimais guardadas (<c>numeric(9,6)</c>).</summary>
    public const int Decimals = 6;

    /// <summary>Raio médio da Terra, usado na fórmula de haversine.</summary>
    public const double EarthRadiusKm = 6371.0088;

    /// <summary>Casas decimais do ponto aproximado: 3, cerca de 100 m.</summary>
    public const int ApproximateDecimals = 3;

    /// <summary>Até quanto o ponto aproximado anda, antes do arredondamento.</summary>
    public const double ApproximateMaxOffsetKm = 0.4;

    /// <summary>
    /// Raio do círculo que o mapa desenha em volta do ponto aproximado. Cobre o deslocamento
    /// (<see cref="ApproximateMaxOffsetKm"/>) mais o arredondamento (~80 m): a casa fica sempre dentro.
    /// </summary>
    public const int ApproximateRadiusMeters = 500;

    /// <summary>Km por grau de latitude (e de longitude no equador).</summary>
    private const double KmPerDegree = 111.32;

    /// <summary>Construtor só para o EF Core materializar o tipo complexo.</summary>
    private GeoPoint()
    {
    }

    private GeoPoint(decimal latitude, decimal longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>De -90 (sul) a 90 (norte).</summary>
    public decimal Latitude { get; private set; }

    /// <summary>De -180 (oeste) a 180 (leste).</summary>
    public decimal Longitude { get; private set; }

    /// <summary>Confere as faixas e arredonda para <see cref="Decimals"/> casas. Devolve os dois erros juntos.</summary>
    public static Result<GeoPoint> Create(decimal latitude, decimal longitude)
    {
        var errors = new List<Error>();

        if (latitude is < -90m or > 90m)
            errors.Add(ListingsErrors.LatitudeOutOfRange);

        if (longitude is < -180m or > 180m)
            errors.Add(ListingsErrors.LongitudeOutOfRange);

        if (errors.Count > 0)
            return Result<GeoPoint>.Failure(errors);

        return Result<GeoPoint>.Success(new GeoPoint(
            Math.Round(latitude, Decimals, MidpointRounding.AwayFromZero),
            Math.Round(longitude, Decimals, MidpointRounding.AwayFromZero)));
    }

    /// <summary>
    /// Distância em linha reta até <paramref name="other"/>, em km (haversine). A busca faz a
    /// mesma conta no banco; esta serve à regra e aos testes.
    /// </summary>
    public double DistanceKmTo(GeoPoint other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var lat1 = ToRadians(Latitude);
        var lat2 = ToRadians(other.Latitude);
        var deltaLat = lat2 - lat1;
        var deltaLng = ToRadians(other.Longitude) - ToRadians(Longitude);

        var a = Math.Pow(Math.Sin(deltaLat / 2), 2)
            + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLng / 2), 2));

        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>
    /// Um ponto perto deste, para o mapa público: deslocado ao acaso até
    /// <see cref="ApproximateMaxOffsetKm"/> (uniforme no disco) e arredondado para
    /// <see cref="ApproximateDecimals"/> casas. Fica a menos de <see cref="ApproximateRadiusMeters"/> m
    /// deste e não deixa voltar a ele: o sorteio é guardado, não recalculado.
    /// </summary>
    public GeoPoint Approximate(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        // A raiz espalha por igual no disco; sem ela os pontos se juntam no centro.
        var distanceKm = ApproximateMaxOffsetKm * Math.Sqrt(random.NextDouble());
        var bearing = 2 * Math.PI * random.NextDouble();

        var latitude = (double)Latitude + (distanceKm * Math.Cos(bearing) / KmPerDegree);
        var longitude = (double)Longitude
            + (distanceKm * Math.Sin(bearing) / (KmPerDegree * Math.Max(Math.Cos(ToRadians(Latitude)), 0.01)));

        return new GeoPoint(Round(latitude, 90), Round(longitude, 180));

        static decimal Round(double degrees, double limit) =>
            Math.Round((decimal)Math.Clamp(degrees, -limit, limit), ApproximateDecimals, MidpointRounding.AwayFromZero);
    }

    private static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
