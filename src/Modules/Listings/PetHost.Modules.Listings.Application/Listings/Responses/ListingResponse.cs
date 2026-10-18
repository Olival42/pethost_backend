using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Listings.Responses;

/// <summary>
/// O cantinho inteiro. O dono (e o admin) recebe tudo; os outros recebem a <b>visão pública</b>:
/// sem rua, número, complemento, CEP e coordenada (só o bairro, a cidade e a UF — escopo,
/// regra 7). Para o mapa, todos recebem o ponto aproximado (<see cref="ApproximateLocation"/>).
/// Campo nulo é omitido no JSON.
/// </summary>
/// <param name="Host">Nome e foto do anfitrião. Omitido em "meus cantinhos", onde vem uma vez no topo.</param>
/// <param name="HomeType"><c>house</c> ou <c>apartment</c>. Omitido quando a empresa não informou.</param>
/// <param name="AcceptedSpecies">Ex.: <c>["dog", "cat"]</c>, na ordem da lista de tipos.</param>
/// <param name="NightlyRate">Diária por pet, em reais.</param>
/// <param name="AvailableWeekdays">De segunda a domingo, ex.: <c>["monday", "friday"]</c>.</param>
/// <param name="Location">Coordenada do endereço. Só para o dono.</param>
/// <param name="ApproximateLocation">Ponto aproximado e o raio do círculo para o mapa. Para todos (o dono vê o que os tutores veem).</param>
/// <param name="Photos">Até 5, na ordem: a primeira é a capa.</param>
/// <param name="Badges">Selos automáticos, só no detalhe (<c>GET /listings/{id}</c>). Omitido nas outras rotas.</param>
/// <param name="Rating">Nota dos tutores, só no detalhe. Omitida nas outras rotas e enquanto ninguém avaliou.</param>
public sealed record ListingResponse(
    Guid Id,
    Guid HostId,
    ListingHostSummary? Host,
    string Title,
    string Description,
    string? HomeType,
    bool HasYard,
    bool HasDogs,
    bool HasCats,
    bool HasKids,
    IReadOnlyList<string> AcceptedSpecies,
    bool AcceptsSmall,
    bool AcceptsMedium,
    bool AcceptsLarge,
    int Capacity,
    decimal NightlyRate,
    string? HouseRules,
    IReadOnlyList<string> AvailableWeekdays,
    ListingAddressResponse Address,
    ListingLocationResponse? Location,
    ListingApproximateLocationResponse ApproximateLocation,
    IReadOnlyList<ListingPhotoResponse> Photos,
    bool IsActive,
    DateTimeOffset? DeactivatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string>? Badges = null,
    ListingRatingResponse? Rating = null)
{
    /// <summary>Visão do dono: endereço completo e coordenada.</summary>
    public static ListingResponse ForOwner(Listing listing, ListingHostSummary? host) => From(listing, host, isOwnerView: true);

    /// <summary>Visão pública: só bairro, cidade e UF.</summary>
    public static ListingResponse ForPublic(Listing listing, ListingHostSummary? host) => From(listing, host, isOwnerView: false);

    /// <summary>Um cantinho, buscando o anfitrião dele.</summary>
    public static async Task<ListingResponse> LoadAsync(
        Listing listing,
        bool isOwnerView,
        IListingHosts listingHosts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(listingHosts);

        var hosts = await listingHosts.GetSummariesAsync([listing.HostId], cancellationToken).ConfigureAwait(false);

        return From(listing, hosts.GetValueOrDefault(listing.HostId), isOwnerView);
    }

    private static ListingResponse From(Listing listing, ListingHostSummary? host, bool isOwnerView)
    {
        ArgumentNullException.ThrowIfNull(listing);

        var details = listing.Details;

        return new ListingResponse(
            listing.Id.Value,
            listing.HostId,
            host,
            details.Title,
            details.Description,
            details.HomeType is { } homeType ? HomeTypeValues.ToWire(homeType) : null,
            details.HasYard,
            details.HasDogs,
            details.HasCats,
            details.HasKids,
            AcceptedSpeciesValues.ToWire(details.AcceptedSpecies),
            details.AcceptsSmall,
            details.AcceptsMedium,
            details.AcceptsLarge,
            details.Capacity,
            details.NightlyRate,
            details.HouseRules,
            WeekdaysValues.ToWire(details.AvailableWeekdays),
            isOwnerView ? ListingAddressResponse.Full(listing.Address) : ListingAddressResponse.Public(listing.Address),
            isOwnerView ? new ListingLocationResponse(listing.Location.Latitude, listing.Location.Longitude) : null,
            ListingApproximateLocationResponse.From(listing),
            [.. listing.Photos.Select(ListingPhotoResponse.From)],
            listing.IsActive,
            listing.DeactivatedAt,
            listing.CreatedAt,
            listing.UpdatedAt);
    }
}

/// <summary>
/// Endereço do cantinho. Na visão pública só vêm bairro, cidade e UF; o resto sai omitido.
/// </summary>
public sealed record ListingAddressResponse(
    string? ZipCode,
    string? Street,
    string? Number,
    string? Complement,
    string Neighborhood,
    string City,
    string State)
{
    public static ListingAddressResponse Full(ListingAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return new(address.ZipCode, address.Street, address.Number, address.Complement, address.Neighborhood, address.City, address.State);
    }

    public static ListingAddressResponse Public(ListingAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return new(null, null, null, null, address.Neighborhood, address.City, address.State);
    }
}

/// <summary>Coordenada do cantinho (só para o dono).</summary>
public sealed record ListingLocationResponse(decimal Latitude, decimal Longitude);

/// <summary>
/// Onde o cantinho fica, mais ou menos: o mapa desenha um círculo de <paramref name="RadiusMeters"/> m
/// em volta do ponto, não um alfinete. O ponto é sorteado e guardado (muda só quando o endereço
/// muda) e a casa fica sempre dentro do círculo, mas nunca no centro de propósito.
/// </summary>
/// <param name="Latitude">Com 3 casas (~100 m).</param>
/// <param name="Longitude">Com 3 casas (~100 m).</param>
/// <param name="RadiusMeters">Raio do círculo: <c>500</c>.</param>
public sealed record ListingApproximateLocationResponse(decimal Latitude, decimal Longitude, int RadiusMeters)
{
    public static ListingApproximateLocationResponse From(Listing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        return new(listing.ApproximateLocation.Latitude, listing.ApproximateLocation.Longitude, GeoPoint.ApproximateRadiusMeters);
    }
}

/// <summary>Uma foto do cantinho.</summary>
/// <param name="Id">Para substituir, tirar ou reordenar: <c>/listings/{listingId}/photos/{id}</c>.</param>
/// <param name="Url">URL pública da imagem; abre direto no navegador.</param>
/// <param name="Position">De 1 a 5. A 1 é a capa.</param>
public sealed record ListingPhotoResponse(Guid Id, string Url, int Position)
{
    public static ListingPhotoResponse From(ListingPhoto photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        return new(photo.Id.Value, photo.Url, photo.Position);
    }
}
