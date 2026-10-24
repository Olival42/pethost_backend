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
        Error.Validation("title", "LISTING_TITLE_REQUIRED", "Title is required.");

    public static readonly Error TitleTooLong =
        Error.Validation("title", "LISTING_TITLE_TOO_LONG", $"Title must be at most {ListingDetails.TitleMaxLength} characters.", ("max", ListingDetails.TitleMaxLength));

    public static readonly Error DescriptionRequired =
        Error.Validation("description", "LISTING_DESCRIPTION_REQUIRED", "Description is required.");

    public static readonly Error DescriptionTooLong =
        Error.Validation("description", "LISTING_DESCRIPTION_TOO_LONG", $"Description must be at most {ListingDetails.DescriptionMaxLength} characters.", ("max", ListingDetails.DescriptionMaxLength));

    public static readonly Error HomeTypeRequired =
        Error.Validation("homeType", "LISTING_HOME_TYPE_REQUIRED", "Home type is required for individual hosts.");

    public static readonly Error HomeTypeInvalid =
        Error.Validation("homeType", "LISTING_HOME_TYPE_INVALID", $"Home type must be one of: {string.Join(", ", HomeTypeValues.All)}.");

    public static readonly Error HasYardRequired =
        Error.Validation("hasYard", "LISTING_HAS_YARD_REQUIRED", "Inform whether the home has a yard.");

    public static readonly Error HasDogsRequired =
        Error.Validation("hasDogs", "LISTING_HAS_DOGS_REQUIRED", "Inform whether there are dogs in the home.");

    public static readonly Error HasCatsRequired =
        Error.Validation("hasCats", "LISTING_HAS_CATS_REQUIRED", "Inform whether there are cats in the home.");

    public static readonly Error HasKidsRequired =
        Error.Validation("hasKids", "LISTING_HAS_KIDS_REQUIRED", "Inform whether there are kids in the home.");

    public static readonly Error AcceptedSpeciesRequired =
        Error.Validation("acceptedSpecies", "LISTING_ACCEPTED_SPECIES_REQUIRED", "Accept at least one species.");

    public static readonly Error AcceptedSpeciesInvalid =
        Error.Validation("acceptedSpecies", "LISTING_ACCEPTED_SPECIES_INVALID", $"Accepted species must be among: {string.Join(", ", AcceptedSpeciesValues.All)}.");

    public static readonly Error AcceptsSmallRequired =
        Error.Validation("acceptsSmall", "LISTING_ACCEPTS_SMALL_REQUIRED", "Inform whether small pets are accepted.");

    public static readonly Error AcceptsMediumRequired =
        Error.Validation("acceptsMedium", "LISTING_ACCEPTS_MEDIUM_REQUIRED", "Inform whether medium pets are accepted.");

    public static readonly Error AcceptsLargeRequired =
        Error.Validation("acceptsLarge", "LISTING_ACCEPTS_LARGE_REQUIRED", "Inform whether large pets are accepted.");

    public static readonly Error SizeRequiredForDogsAndCats =
        Error.Validation("acceptsSmall", "LISTING_SIZE_REQUIRED_FOR_DOGS_AND_CATS", "Accept at least one size (small, medium or large) when dogs or cats are accepted.");

    public static readonly Error CapacityRequired =
        Error.Validation("capacity", "LISTING_CAPACITY_REQUIRED", "Capacity is required.");

    /// <summary>O teto depende do dono: pessoa física ou empresa (por filial).</summary>
    public static Error CapacityOutOfRange(bool isCompany) =>
        Error.Validation("capacity", "LISTING_CAPACITY_OUT_OF_RANGE", $"Capacity must be between 1 and {ListingDetails.MaxCapacityFor(isCompany)} pets.", ("max", ListingDetails.MaxCapacityFor(isCompany)));

    public static readonly Error NightlyRateRequired =
        Error.Validation("nightlyRate", "LISTING_NIGHTLY_RATE_REQUIRED", "Nightly rate is required.");

    public static readonly Error NightlyRateOutOfRange =
        Error.Validation(
            "nightlyRate", "LISTING_NIGHTLY_RATE_OUT_OF_RANGE",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Nightly rate must be between {ListingDetails.MinNightlyRate:0.00} and {ListingDetails.MaxNightlyRate:0.00}."), ("min", ListingDetails.MinNightlyRate), ("max", ListingDetails.MaxNightlyRate));

    public static readonly Error NightlyRateTooPrecise =
        Error.Validation("nightlyRate", "LISTING_NIGHTLY_RATE_TOO_PRECISE", $"Nightly rate must have at most {ListingDetails.NightlyRateDecimals} decimal places.", ("max", ListingDetails.NightlyRateDecimals));

    public static readonly Error HouseRulesTooLong =
        Error.Validation("houseRules", "LISTING_HOUSE_RULES_TOO_LONG", $"House rules must be at most {ListingDetails.HouseRulesMaxLength} characters.", ("max", ListingDetails.HouseRulesMaxLength));

    public static readonly Error AvailableWeekdaysRequired =
        Error.Validation("availableWeekdays", "LISTING_AVAILABLE_WEEKDAYS_REQUIRED", "Choose at least one weekday.");

    public static readonly Error AvailableWeekdaysInvalid =
        Error.Validation("availableWeekdays", "LISTING_AVAILABLE_WEEKDAYS_INVALID", $"Weekdays must be among: {string.Join(", ", WeekdaysValues.All)}.");

    // --- Validação: endereço (o campo leva "address." porque o JSON é aninhado) ---

    public static readonly Error ZipCodeInvalid =
        Error.Validation("address.zipCode", "LISTING_ZIP_CODE_INVALID", $"Zip code (CEP) must have {ListingAddress.ZipCodeLength} digits.", ("digits", ListingAddress.ZipCodeLength));

    public static readonly Error StreetRequired =
        Error.Validation("address.street", "LISTING_STREET_REQUIRED", "Street is required.");

    public static readonly Error StreetTooLong =
        Error.Validation("address.street", "LISTING_STREET_TOO_LONG", $"Street must be at most {ListingAddress.StreetMaxLength} characters.", ("max", ListingAddress.StreetMaxLength));

    public static readonly Error NumberRequired =
        Error.Validation("address.number", "LISTING_NUMBER_REQUIRED", "Number is required. Use 'S/N' when there is none.");

    public static readonly Error NumberTooLong =
        Error.Validation("address.number", "LISTING_NUMBER_TOO_LONG", $"Number must be at most {ListingAddress.NumberMaxLength} characters.", ("max", ListingAddress.NumberMaxLength));

    public static readonly Error ComplementTooLong =
        Error.Validation("address.complement", "LISTING_COMPLEMENT_TOO_LONG", $"Complement must be at most {ListingAddress.ComplementMaxLength} characters.", ("max", ListingAddress.ComplementMaxLength));

    public static readonly Error NeighborhoodRequired =
        Error.Validation("address.neighborhood", "LISTING_NEIGHBORHOOD_REQUIRED", "Neighborhood is required.");

    public static readonly Error NeighborhoodTooLong =
        Error.Validation("address.neighborhood", "LISTING_NEIGHBORHOOD_TOO_LONG", $"Neighborhood must be at most {ListingAddress.NeighborhoodMaxLength} characters.", ("max", ListingAddress.NeighborhoodMaxLength));

    public static readonly Error CityRequired =
        Error.Validation("address.city", "LISTING_CITY_REQUIRED", "City is required.");

    public static readonly Error CityTooLong =
        Error.Validation("address.city", "LISTING_CITY_TOO_LONG", $"City must be at most {ListingAddress.CityMaxLength} characters.", ("max", ListingAddress.CityMaxLength));

    public static readonly Error StateInvalid =
        Error.Validation("address.state", "LISTING_STATE_INVALID", "State must be a valid Brazilian state code, e.g. 'PR'.");

    // --- Validação: coordenada e busca ---

    public static readonly Error LatitudeOutOfRange =
        Error.Validation("latitude", "LISTING_LATITUDE_OUT_OF_RANGE", "Latitude must be between -90 and 90.");

    public static readonly Error LongitudeOutOfRange =
        Error.Validation("longitude", "LISTING_LONGITUDE_OUT_OF_RANGE", "Longitude must be between -180 and 180.");

    public static readonly Error CoordinatesIncomplete =
        Error.Validation("latitude", "LISTING_COORDINATES_INCOMPLETE", "Send latitude and longitude together.");

    public static readonly Error RadiusOutOfRange =
        Error.Validation("radiusKm", "LISTING_RADIUS_OUT_OF_RANGE", $"Radius must be between {MinRadiusKm} and {MaxRadiusKm} km.", ("min", MinRadiusKm), ("max", MaxRadiusKm));

    public static readonly Error StateFilterInvalid =
        Error.Validation("state", "LISTING_STATE_FILTER_INVALID", "State must be a valid Brazilian state code, e.g. 'PR'.");

    public static readonly Error CityFilterTooLong =
        Error.Validation("city", "LISTING_CITY_FILTER_TOO_LONG", $"City must be at most {ListingAddress.CityMaxLength} characters.", ("max", ListingAddress.CityMaxLength));

    public static readonly Error NeighborhoodFilterTooLong =
        Error.Validation("neighborhood", "LISTING_NEIGHBORHOOD_FILTER_TOO_LONG", $"Neighborhood must be at most {ListingAddress.NeighborhoodMaxLength} characters.", ("max", ListingAddress.NeighborhoodMaxLength));

    public static readonly Error SpeciesFilterInvalid =
        Error.Validation("species", "LISTING_SPECIES_FILTER_INVALID", $"Species must be among: {string.Join(", ", AcceptedSpeciesValues.All)}.");

    public static readonly Error SizeFilterInvalid =
        Error.Validation("sizes", "LISTING_SIZE_FILTER_INVALID", "Sizes must be among: small, medium, large.");

    public static readonly Error WeekdaysFilterInvalid =
        Error.Validation("weekdays", "LISTING_WEEKDAYS_FILTER_INVALID", $"Weekdays must be among: {string.Join(", ", WeekdaysValues.All)}.");

    public static readonly Error MinNightlyRateFilterInvalid =
        Error.Validation("minNightlyRate", "LISTING_MIN_NIGHTLY_RATE_FILTER_INVALID", "Minimum nightly rate must be 0 or greater.");

    public static readonly Error MaxNightlyRateFilterInvalid =
        Error.Validation("maxNightlyRate", "LISTING_MAX_NIGHTLY_RATE_FILTER_INVALID", "Maximum nightly rate must be 0 or greater and not below the minimum.");

    public static readonly Error MinCapacityFilterInvalid =
        Error.Validation("minCapacity", "LISTING_MIN_CAPACITY_FILTER_INVALID", $"Minimum capacity must be between 1 and {ListingDetails.MaxCapacityCompany}.", ("max", ListingDetails.MaxCapacityCompany));

    public static readonly Error CheckInFilterRequired =
        Error.Validation("checkIn", "LISTING_CHECK_IN_FILTER_REQUIRED", "Send check-in and check-out together.");

    public static readonly Error CheckOutFilterRequired =
        Error.Validation("checkOut", "LISTING_CHECK_OUT_FILTER_REQUIRED", "Send check-in and check-out together.");

    public static readonly Error CheckInFilterInvalid =
        Error.Validation("checkIn", "LISTING_CHECK_IN_FILTER_INVALID", $"Check-in must be a date in the format {StayWindow.DateFormat}.", ("format", StayWindow.DateFormat));

    public static readonly Error CheckOutFilterInvalid =
        Error.Validation("checkOut", "LISTING_CHECK_OUT_FILTER_INVALID", $"Check-out must be a date in the format {StayWindow.DateFormat}.", ("format", StayWindow.DateFormat));

    public static readonly Error CheckInFilterInThePast =
        Error.Validation("checkIn", "LISTING_CHECK_IN_FILTER_IN_THE_PAST", "Check-in cannot be in the past.");

    public static readonly Error CheckInFilterTooFarAhead =
        Error.Validation("checkIn", "LISTING_CHECK_IN_FILTER_TOO_FAR_AHEAD", $"Check-in can be at most {StayWindow.MaxDaysAhead} days ahead.", ("max", StayWindow.MaxDaysAhead));

    public static readonly Error CheckOutFilterNotAfterCheckIn =
        Error.Validation("checkOut", "LISTING_CHECK_OUT_FILTER_NOT_AFTER_CHECK_IN", "Check-out must be after check-in.");

    public static readonly Error StayFilterTooLong =
        Error.Validation("checkOut", "LISTING_STAY_FILTER_TOO_LONG", $"A stay can have at most {StayWindow.MaxNights} nights.", ("max", StayWindow.MaxNights));

    public static readonly Error PetsFilterInvalid =
        Error.Validation("pets", "LISTING_PETS_FILTER_INVALID", $"Pets must be between 1 and {ListingDetails.MaxCapacityCompany}.", ("max", ListingDetails.MaxCapacityCompany));

    public static readonly Error PageInvalid =
        Error.Validation("page", "LISTING_PAGE_INVALID", "Page must be 1 or greater.");

    public static readonly Error PageSizeInvalid =
        Error.Validation("pageSize", "LISTING_PAGE_SIZE_INVALID", $"Page size must be between 1 and {MaxPageSize}.", ("max", MaxPageSize));

    // --- Validação: "Combina com seu pet" ---

    public static readonly Error CompatibilityPetRequired =
        Error.Validation("petId", "LISTING_COMPATIBILITY_PET_REQUIRED", "Pet id is required.");

    // --- Validação: fotos ---

    // A URL da foto vem do bucket, não do cliente: estes dois só aparecem com o storage mal configurado.
    public static readonly Error PhotoUrlInvalid =
        Error.Validation("photoUrl", "LISTING_PHOTO_URL_INVALID", "Photo URL must be an absolute http or https address.");

    public static readonly Error PhotoUrlTooLong =
        Error.Validation("photoUrl", "LISTING_PHOTO_URL_TOO_LONG", $"Photo URL must be at most {ListingPhoto.UrlMaxLength} characters.", ("max", ListingPhoto.UrlMaxLength));

    public static readonly Error PhotoOrderInvalid =
        Error.Validation("photoIds", "LISTING_PHOTO_ORDER_INVALID", "Send every photo of the listing exactly once, in the new order.");

    // --- Validação: agenda (bloqueios de datas) ---

    public static readonly Error AvailabilityBlockStartRequired =
        Error.Validation("startDate", "LISTING_AVAILABILITY_BLOCK_START_REQUIRED", "Start date is required.");

    public static readonly Error AvailabilityBlockStartInvalid =
        Error.Validation("startDate", "LISTING_AVAILABILITY_BLOCK_START_INVALID", $"Start date must be a date in the format {StayWindow.DateFormat}.", ("format", StayWindow.DateFormat));

    public static readonly Error AvailabilityBlockEndRequired =
        Error.Validation("endDate", "LISTING_AVAILABILITY_BLOCK_END_REQUIRED", "End date is required.");

    public static readonly Error AvailabilityBlockEndInvalid =
        Error.Validation("endDate", "LISTING_AVAILABILITY_BLOCK_END_INVALID", $"End date must be a date in the format {StayWindow.DateFormat}.", ("format", StayWindow.DateFormat));

    public static readonly Error AvailabilityBlockStartInThePast =
        Error.Validation("startDate", "LISTING_AVAILABILITY_BLOCK_START_IN_THE_PAST", "Start date cannot be in the past.");

    public static readonly Error AvailabilityBlockStartTooFarAhead =
        Error.Validation("startDate", "LISTING_AVAILABILITY_BLOCK_START_TOO_FAR_AHEAD", $"Dates can be blocked at most {AvailabilityBlock.MaxDaysAhead} days ahead.", ("max", AvailabilityBlock.MaxDaysAhead));

    public static readonly Error AvailabilityBlockEndTooFarAhead =
        Error.Validation("endDate", "LISTING_AVAILABILITY_BLOCK_END_TOO_FAR_AHEAD", $"Dates can be blocked at most {AvailabilityBlock.MaxDaysAhead} days ahead.", ("max", AvailabilityBlock.MaxDaysAhead));

    public static readonly Error AvailabilityBlockEndBeforeStart =
        Error.Validation("endDate", "LISTING_AVAILABILITY_BLOCK_END_BEFORE_START", "End date cannot be before the start date.");

    public static readonly Error AvailabilityBlockReasonTooLong =
        Error.Validation("reason", "LISTING_AVAILABILITY_BLOCK_REASON_TOO_LONG", $"Reason must be at most {AvailabilityBlock.ReasonMaxLength} characters.", ("max", AvailabilityBlock.ReasonMaxLength));

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

    /// <summary>
    /// 422: o Stripe ainda não aprovou o cadastro de recebimento do anfitrião (repasses não
    /// liberados). Cantinho só depois disso: sem repasse não dá para pagar a hospedagem.
    /// </summary>
    public static readonly Error HostPayoutsNotEnabled =
        new("LISTING_HOST_PAYOUTS_NOT_ENABLED", "Finish the payout setup and wait for Stripe to approve it before creating a listing.");

    /// <summary>
    /// 422: o cantinho tem reserva em aberto (solicitada, aceita, confirmada ou em andamento). Antes de
    /// inativar, o anfitrião responde aos pedidos, cancela as reservas que não começaram ou faz o check-out.
    /// </summary>
    public static readonly Error HasOpenBooking =
        new("LISTING_HAS_OPEN_BOOKING", "This listing has open bookings. Answer the requests, cancel the bookings that have not started or check out the stay in progress before deactivating it.");

    /// <summary>404: a conta do token não tem perfil de tutor.</summary>
    public static readonly Error OwnerNotFound =
        new("LISTING_OWNER_NOT_FOUND", "This account has no owner profile.");

    /// <summary>
    /// 404: o pet não existe, está inativo ou é de outro tutor. As três situações respondem igual
    /// de propósito (§8).
    /// </summary>
    public static Error PetNotFound(Guid petId) =>
        new("LISTING_PET_NOT_FOUND", $"Pet '{petId}' was not found.");

    /// <summary>422: pessoa física já tem o seu cantinho. Só empresa tem vários (um por filial).</summary>
    public static readonly Error LimitReached =
        new Error("LISTING_LIMIT_REACHED", $"An individual host can have only {Listing.MaxListingsPerIndividual} listing. Reactivate or edit it instead; only companies can have one per branch.")
            .WithParams(("max", Listing.MaxListingsPerIndividual));

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
        new Error("LISTING_PHOTO_LIMIT_REACHED", $"A listing can have at most {Listing.MaxPhotos} photos. Remove one before adding another.")
            .WithParams(("max", Listing.MaxPhotos));

    /// <summary>409: o cantinho já tem esta mesma imagem.</summary>
    public static readonly Error PhotoAlreadyExists =
        new("LISTING_PHOTO_ALREADY_EXISTS", "This listing already has this photo.");

    /// <summary>404: o cantinho não tem foto com esse id.</summary>
    public static Error PhotoNotFound(ListingPhotoId id) =>
        new("LISTING_PHOTO_NOT_FOUND", $"Photo '{id.Value}' was not found.");

    /// <summary>422: o cantinho já tem o máximo de bloqueios em aberto.</summary>
    public static readonly Error AvailabilityBlockLimitReached =
        new Error("LISTING_AVAILABILITY_BLOCK_LIMIT_REACHED", $"A listing can have at most {AvailabilityBlock.MaxOpenBlocks} upcoming blocked periods. Remove one or merge them.")
            .WithParams(("max", AvailabilityBlock.MaxOpenBlocks));

    /// <summary>422: o período sobrepõe outro bloqueio do cantinho (com as datas dele).</summary>
    public static Error AvailabilityBlockOverlap(DateOnly startDate, DateOnly endDate) =>
        new Error(
            "LISTING_AVAILABILITY_BLOCK_OVERLAP",
            string.Create(CultureInfo.InvariantCulture, $"These dates overlap the period already blocked from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}."))
            .WithParams(
                ("startDate", startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("endDate", endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    /// <summary>404: o cantinho não tem bloqueio com esse id.</summary>
    public static Error AvailabilityBlockNotFound(AvailabilityBlockId id) =>
        new("LISTING_AVAILABILITY_BLOCK_NOT_FOUND", $"Blocked period '{id.Value}' was not found.");

    // --- Validação: identificadores e campos obrigatórios de comandos internos e de rota ---

    public static readonly Error ListingIdRequired =
        Error.Validation("listingId", "LISTING_LISTING_ID_REQUIRED", "Listing id is required.");

    public static readonly Error BlockIdRequired =
        Error.Validation("blockId", "LISTING_BLOCK_ID_REQUIRED", "Block id is required.");

    public static readonly Error UserIdRequired =
        Error.Validation("userId", "LISTING_USER_ID_REQUIRED", "User id is required.");

    public static readonly Error HostIdRequired =
        Error.Validation("hostId", "LISTING_HOST_ID_REQUIRED", "Host id is required.");

    public static readonly Error PhotoIdRequired =
        Error.Validation("photoId", "LISTING_PHOTO_ID_REQUIRED", "Photo id is required.");
}
