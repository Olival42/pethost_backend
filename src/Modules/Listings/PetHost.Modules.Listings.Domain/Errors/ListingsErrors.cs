using System.Globalization;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Listings.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Listings (cantinhos). Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class ListingsErrors
{
    /// <summary>Maior página da busca (§13).</summary>
    public const int MaxPageSize = 100;

    /// <summary>Menor raio da busca, em km.</summary>
    public const int MinRadiusKm = 5;

    /// <summary>Maior raio da busca, em km.</summary>
    public const int MaxRadiusKm = 50;

    // --- Validação: descrição do cantinho (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error TitleRequired =
        Error.Validation("title", "Title is required.");

    public static readonly Error TitleTooLong =
        Error.Validation("title", $"Title must be at most {ListingDetails.TitleMaxLength} characters.");

    public static readonly Error DescriptionRequired =
        Error.Validation("description", "Description is required.");

    public static readonly Error DescriptionTooLong =
        Error.Validation("description", $"Description must be at most {ListingDetails.DescriptionMaxLength} characters.");

    public static readonly Error HomeTypeRequired =
        Error.Validation("homeType", "Home type is required for individual hosts.");

    public static readonly Error HomeTypeInvalid =
        Error.Validation("homeType", $"Home type must be one of: {string.Join(", ", HomeTypeValues.All)}.");

    public static readonly Error HasYardRequired =
        Error.Validation("hasYard", "Inform whether the home has a yard.");

    public static readonly Error HasDogsRequired =
        Error.Validation("hasDogs", "Inform whether there are dogs in the home.");

    public static readonly Error HasCatsRequired =
        Error.Validation("hasCats", "Inform whether there are cats in the home.");

    public static readonly Error HasKidsRequired =
        Error.Validation("hasKids", "Inform whether there are kids in the home.");

    public static readonly Error AcceptedSpeciesRequired =
        Error.Validation("acceptedSpecies", "Accept at least one species.");

    public static readonly Error AcceptedSpeciesInvalid =
        Error.Validation("acceptedSpecies", $"Accepted species must be among: {string.Join(", ", AcceptedSpeciesValues.All)}.");

    public static readonly Error AcceptsSmallRequired =
        Error.Validation("acceptsSmall", "Inform whether small pets are accepted.");

    public static readonly Error AcceptsMediumRequired =
        Error.Validation("acceptsMedium", "Inform whether medium pets are accepted.");

    public static readonly Error AcceptsLargeRequired =
        Error.Validation("acceptsLarge", "Inform whether large pets are accepted.");

    public static readonly Error SizeRequiredForDogsAndCats =
        Error.Validation("acceptsSmall", "Accept at least one size (small, medium or large) when dogs or cats are accepted.");

    public static readonly Error CapacityRequired =
        Error.Validation("capacity", "Capacity is required.");

    /// <summary>O teto depende do dono: pessoa física ou empresa (por filial).</summary>
    public static Error CapacityOutOfRange(bool isCompany) =>
        Error.Validation("capacity", $"Capacity must be between 1 and {ListingDetails.MaxCapacityFor(isCompany)} pets.");

    public static readonly Error NightlyRateRequired =
        Error.Validation("nightlyRate", "Nightly rate is required.");

    public static readonly Error NightlyRateOutOfRange =
        Error.Validation(
            "nightlyRate",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Nightly rate must be between {ListingDetails.MinNightlyRate:0.00} and {ListingDetails.MaxNightlyRate:0.00}."));

    public static readonly Error NightlyRateTooPrecise =
        Error.Validation("nightlyRate", $"Nightly rate must have at most {ListingDetails.NightlyRateDecimals} decimal places.");

    public static readonly Error HouseRulesTooLong =
        Error.Validation("houseRules", $"House rules must be at most {ListingDetails.HouseRulesMaxLength} characters.");

    public static readonly Error AvailableWeekdaysRequired =
        Error.Validation("availableWeekdays", "Choose at least one weekday.");

    public static readonly Error AvailableWeekdaysInvalid =
        Error.Validation("availableWeekdays", $"Weekdays must be among: {string.Join(", ", WeekdaysValues.All)}.");

    // --- Validação: endereço (o campo leva "address." porque o JSON é aninhado) ---

    public static readonly Error ZipCodeInvalid =
        Error.Validation("address.zipCode", $"Zip code (CEP) must have {ListingAddress.ZipCodeLength} digits.");

    public static readonly Error StreetRequired =
        Error.Validation("address.street", "Street is required.");

    public static readonly Error StreetTooLong =
        Error.Validation("address.street", $"Street must be at most {ListingAddress.StreetMaxLength} characters.");

    public static readonly Error NumberRequired =
        Error.Validation("address.number", "Number is required. Use 'S/N' when there is none.");

    public static readonly Error NumberTooLong =
        Error.Validation("address.number", $"Number must be at most {ListingAddress.NumberMaxLength} characters.");

    public static readonly Error ComplementTooLong =
        Error.Validation("address.complement", $"Complement must be at most {ListingAddress.ComplementMaxLength} characters.");

    public static readonly Error NeighborhoodRequired =
        Error.Validation("address.neighborhood", "Neighborhood is required.");

    public static readonly Error NeighborhoodTooLong =
        Error.Validation("address.neighborhood", $"Neighborhood must be at most {ListingAddress.NeighborhoodMaxLength} characters.");

    public static readonly Error CityRequired =
        Error.Validation("address.city", "City is required.");

    public static readonly Error CityTooLong =
        Error.Validation("address.city", $"City must be at most {ListingAddress.CityMaxLength} characters.");

    public static readonly Error StateInvalid =
        Error.Validation("address.state", "State must be a valid Brazilian state code, e.g. 'PR'.");

    // --- Validação: coordenada e busca ---

    public static readonly Error LatitudeOutOfRange =
        Error.Validation("latitude", "Latitude must be between -90 and 90.");

    public static readonly Error LongitudeOutOfRange =
        Error.Validation("longitude", "Longitude must be between -180 and 180.");

    public static readonly Error CoordinatesIncomplete =
        Error.Validation("latitude", "Send latitude and longitude together, or neither to search around your address.");

    public static readonly Error RadiusOutOfRange =
        Error.Validation("radiusKm", $"Radius must be between {MinRadiusKm} and {MaxRadiusKm} km.");

    public static readonly Error PageInvalid =
        Error.Validation("page", "Page must be 1 or greater.");

    public static readonly Error PageSizeInvalid =
        Error.Validation("pageSize", $"Page size must be between 1 and {MaxPageSize}.");

    // --- Validação: fotos ---

    // A URL da foto vem do bucket, não do cliente: estes dois só aparecem com o storage mal configurado.
    public static readonly Error PhotoUrlInvalid =
        Error.Validation("photoUrl", "Photo URL must be an absolute http or https address.");

    public static readonly Error PhotoUrlTooLong =
        Error.Validation("photoUrl", $"Photo URL must be at most {ListingPhoto.UrlMaxLength} characters.");

    public static readonly Error PhotoOrderInvalid =
        Error.Validation("photoIds", "Send every photo of the listing exactly once, in the new order.");

    // --- Regras de negócio ---

    /// <summary>
    /// 404: não existe cantinho com esse id, ou ele está inativo e quem pede não é o dono. As
    /// duas situações respondem igual de propósito.
    /// </summary>
    public static Error NotFound(ListingId id) =>
        new("LISTING_NOT_FOUND", $"Listing '{id.Value}' was not found.");

    /// <summary>404: a conta do token não tem perfil de anfitrião (ainda não foi verificada).</summary>
    public static readonly Error HostNotFound =
        new("LISTING_HOST_NOT_FOUND", "This account has no verified host profile yet.");

    /// <summary>403: o anfitrião está inativo ou suspenso e não pode mexer nos cantinhos.</summary>
    public static readonly Error HostDeactivated =
        new("LISTING_HOST_DEACTIVATED", "This host profile is deactivated or suspended.");

    /// <summary>422: pessoa física já tem o seu cantinho. Só empresa tem vários (um por filial).</summary>
    public static readonly Error LimitReached =
        new("LISTING_LIMIT_REACHED", $"An individual host can have only {Listing.MaxListingsPerIndividual} listing. Reactivate or edit it instead; only companies can have one per branch.");

    /// <summary>422: o geocodificador não achou o endereço do cantinho.</summary>
    public static readonly Error AddressNotLocated =
        new("LISTING_ADDRESS_NOT_LOCATED", "We could not find this address on the map. Check the zip code, street and number.");

    /// <summary>503: o serviço de mapa não respondeu. Tente de novo em instantes.</summary>
    public static readonly Error GeocoderUnavailable =
        new("LISTING_GEOCODER_UNAVAILABLE", "The map service is unavailable right now. Try again in a moment.");

    /// <summary>422: a busca sem coordenada usa o endereço da conta, e ela não tem.</summary>
    public static readonly Error SearchAddressRequired =
        new("LISTING_SEARCH_ADDRESS_REQUIRED", "Your account has no address. Add one to your profile or send latitude and longitude.");

    /// <summary>422: o endereço da conta não foi achado no mapa.</summary>
    public static readonly Error SearchAddressNotLocated =
        new("LISTING_SEARCH_ADDRESS_NOT_LOCATED", "We could not find your address on the map. Check it in your profile or send latitude and longitude.");

    /// <summary>422: o cantinho já tem o máximo de fotos.</summary>
    public static readonly Error PhotoLimitReached =
        new("LISTING_PHOTO_LIMIT_REACHED", $"A listing can have at most {Listing.MaxPhotos} photos. Remove one before adding another.");

    /// <summary>409: o cantinho já tem esta mesma imagem.</summary>
    public static readonly Error PhotoAlreadyExists =
        new("LISTING_PHOTO_ALREADY_EXISTS", "This listing already has this photo.");

    /// <summary>404: o cantinho não tem foto com esse id.</summary>
    public static Error PhotoNotFound(ListingPhotoId id) =>
        new("LISTING_PHOTO_NOT_FOUND", $"Photo '{id.Value}' was not found.");
}
