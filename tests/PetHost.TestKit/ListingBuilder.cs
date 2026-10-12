using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.TestKit;

/// <summary>
/// Monta um cantinho para teste: por padrão, a casa da Dona Cida na Zona 7 de Maringá —
/// pessoa física, válida em tudo. Cada <c>With...</c> troca uma parte; o teste declara só o que importa.
/// </summary>
public sealed class ListingBuilder
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Centro de Maringá (Catedral).</summary>
    public const decimal MaringaLatitude = -23.425269m;

    public const decimal MaringaLongitude = -51.938604m;

    private ListingDetailsData _details = new(
        Title: "Casa com quintal na Zona 7",
        Description: "Casa térrea, quintal gramado e muito carinho.",
        HomeType: "house",
        HasYard: true,
        HasDogs: true,
        HasCats: false,
        HasKids: false,
        AcceptedSpecies: ["dog", "cat"],
        AcceptsSmall: true,
        AcceptsMedium: true,
        AcceptsLarge: false,
        Capacity: 3,
        NightlyRate: 60.00m,
        HouseRules: "Nada de subir no sofá.",
        AvailableWeekdays: ["monday", "tuesday", "wednesday", "thursday", "friday"]);

    private ListingAddressData _address = new(
        ZipCode: "87020-000",
        Street: "Rua das Flores",
        Number: "120",
        Complement: null,
        Neighborhood: "Zona 7",
        City: "Maringá",
        State: "PR");

    private Guid _hostId = Guid.CreateVersion7();
    private bool _isCompany;
    private decimal _latitude = MaringaLatitude;
    private decimal _longitude = MaringaLongitude;

    public ListingBuilder WithDetails(Func<ListingDetailsData, ListingDetailsData> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _details = change(_details);
        return this;
    }

    public ListingBuilder WithAddress(Func<ListingAddressData, ListingAddressData> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _address = change(_address);
        return this;
    }

    public ListingBuilder OwnedBy(Guid hostId, bool isCompany = false)
    {
        _hostId = hostId;
        _isCompany = isCompany;
        return this;
    }

    public ListingBuilder At(decimal latitude, decimal longitude)
    {
        _latitude = latitude;
        _longitude = longitude;
        return this;
    }

    public ListingDetailsData BuildDetailsData() => _details;

    public ListingAddressData BuildAddressData() => _address;

    /// <summary>A descrição validada. Estoura se o builder montou algo inválido (bug do teste).</summary>
    public ListingDetails BuildDetails() => Valid(ListingDetails.Create(_details, _isCompany).Value, "details");

    public ListingAddress BuildAddress() => Valid(ListingAddress.Create(_address).Value, "address");

    public GeoPoint BuildLocation() => Valid(GeoPoint.Create(_latitude, _longitude).Value, "location");

    /// <summary>O cantinho, novo, sem fotos.</summary>
    public Listing Build() =>
        Valid(
            Listing.Create(new ListingHost(_hostId, _isCompany), 0, BuildDetails(), BuildAddress(), BuildLocation(), DefaultNow).Value,
            "listing");

    private static T Valid<T>(T? value, string part) where T : class =>
        value ?? throw new InvalidOperationException($"Invalid test listing {part}.");
}
