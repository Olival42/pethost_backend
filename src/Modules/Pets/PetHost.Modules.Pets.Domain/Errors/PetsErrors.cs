using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Pets.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Pets. Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class PetsErrors
{
    // --- Validação de formato (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error SpeciesRequired =
        Error.Validation("species", "PET_SPECIES_REQUIRED", "Species is required.");

    public static readonly Error SpeciesInvalid =
        Error.Validation("species", "PET_SPECIES_INVALID", $"Species must be one of: {string.Join(", ", PetSpeciesValues.All)}.");

    public static readonly Error SpeciesDescriptionRequired =
        Error.Validation("speciesDescription", "PET_SPECIES_DESCRIPTION_REQUIRED", "Describe the animal when the species is 'exotic'.");

    public static readonly Error SpeciesDescriptionTooLong =
        Error.Validation("speciesDescription", "PET_SPECIES_DESCRIPTION_TOO_LONG", $"Species description must be at most {PetProfile.SpeciesDescriptionMaxLength} characters.", ("max", PetProfile.SpeciesDescriptionMaxLength));

    public static readonly Error SpeciesDescriptionNotAllowed =
        Error.Validation("speciesDescription", "PET_SPECIES_DESCRIPTION_NOT_ALLOWED", "Species description is only for exotic animals.");

    public static readonly Error NameRequired =
        Error.Validation("name", "PET_NAME_REQUIRED", "Name is required.");

    public static readonly Error NameTooLong =
        Error.Validation("name", "PET_NAME_TOO_LONG", $"Name must be at most {PetProfile.NameMaxLength} characters.", ("max", PetProfile.NameMaxLength));

    // A URL da foto vem do bucket, não do cliente: estes dois só aparecem com o storage mal configurado.
    public static readonly Error PhotoUrlInvalid =
        Error.Validation("photoUrl", "PET_PHOTO_URL_INVALID", "Photo URL must be an absolute http or https address.");

    public static readonly Error PhotoUrlTooLong =
        Error.Validation("photoUrl", "PET_PHOTO_URL_TOO_LONG", $"Photo URL must be at most {PetPhoto.UrlMaxLength} characters.", ("max", PetPhoto.UrlMaxLength));

    public static readonly Error PhotoOrderInvalid =
        Error.Validation("photoIds", "PET_PHOTO_ORDER_INVALID", "Send every photo of the pet exactly once, in the new order.");

    public static readonly Error BreedTooLong =
        Error.Validation("breed", "PET_BREED_TOO_LONG", $"Breed must be at most {PetProfile.BreedMaxLength} characters.", ("max", PetProfile.BreedMaxLength));

    public static readonly Error SizeRequired =
        Error.Validation("size", "PET_SIZE_REQUIRED", "Size is required for dogs.");

    public static readonly Error SizeInvalid =
        Error.Validation("size", "PET_SIZE_INVALID", $"Size must be one of: {string.Join(", ", PetSizeValues.All)}.");

    public static readonly Error SizeNotAllowed =
        Error.Validation("size", "PET_SIZE_NOT_ALLOWED", "Size only applies to dogs and cats.");

    public static readonly Error BirthDateInvalidFormat =
        Error.Validation("birthDate", "PET_BIRTH_DATE_INVALID_FORMAT", "Birth date must be a valid date in the format yyyy-MM-dd.");

    public static readonly Error BirthDateInFuture =
        Error.Validation("birthDate", "PET_BIRTH_DATE_IN_FUTURE", "Birth date cannot be in the future.");

    public static readonly Error SexRequired =
        Error.Validation("sex", "PET_SEX_REQUIRED", "Sex is required.");

    public static readonly Error SexInvalid =
        Error.Validation("sex", "PET_SEX_INVALID", $"Sex must be one of: {string.Join(", ", PetSexValues.All)}.");

    public static readonly Error IsNeuteredRequired =
        Error.Validation("isNeutered", "PET_IS_NEUTERED_REQUIRED", "Inform whether the pet is neutered.");

    public static readonly Error IsVaccinatedRequired =
        Error.Validation("isVaccinated", "PET_IS_VACCINATED_REQUIRED", "Inform whether the pet's vaccines are up to date.");

    public static readonly Error GoodWithDogsRequired =
        Error.Validation("goodWithDogs", "PET_GOOD_WITH_DOGS_REQUIRED", "Inform whether the pet gets along with dogs.");

    public static readonly Error GoodWithCatsRequired =
        Error.Validation("goodWithCats", "PET_GOOD_WITH_CATS_REQUIRED", "Inform whether the pet gets along with cats.");

    public static readonly Error GoodWithKidsRequired =
        Error.Validation("goodWithKids", "PET_GOOD_WITH_KIDS_REQUIRED", "Inform whether the pet gets along with kids.");

    public static readonly Error MedicationNotesTooLong =
        Error.Validation("medicationNotes", "PET_MEDICATION_NOTES_TOO_LONG", $"Medication notes must be at most {PetProfile.CareNotesMaxLength} characters.", ("max", PetProfile.CareNotesMaxLength));

    public static readonly Error FeedingNotesTooLong =
        Error.Validation("feedingNotes", "PET_FEEDING_NOTES_TOO_LONG", $"Feeding notes must be at most {PetProfile.CareNotesMaxLength} characters.", ("max", PetProfile.CareNotesMaxLength));

    public static readonly Error VetContactTooLong =
        Error.Validation("vetContact", "PET_VET_CONTACT_TOO_LONG", $"Vet contact must be at most {PetProfile.VetContactMaxLength} characters.", ("max", PetProfile.VetContactMaxLength));

    public static readonly Error NotesTooLong =
        Error.Validation("notes", "PET_NOTES_TOO_LONG", $"Notes must be at most {PetProfile.NotesMaxLength} characters.", ("max", PetProfile.NotesMaxLength));

    public static readonly Error WeightOutOfRange =
        Error.Validation("weightKg", "PET_WEIGHT_OUT_OF_RANGE", $"Weight must be greater than 0 and at most {PetProfile.WeightMaxKg} kg.", ("max", PetProfile.WeightMaxKg));

    public static readonly Error MicrochipInvalid =
        Error.Validation("microchip", "PET_MICROCHIP_INVALID", $"Microchip number must have {PetProfile.MicrochipLength} digits.", ("digits", PetProfile.MicrochipLength));

    public static readonly Error AllergiesTooLong =
        Error.Validation("allergies", "PET_ALLERGIES_TOO_LONG", $"Allergies must be at most {PetProfile.CareNotesMaxLength} characters.", ("max", PetProfile.CareNotesMaxLength));

    // --- Regras de negócio ---

    /// <summary>
    /// 404: não existe pet com esse id, ou ele é de outra conta (quem pede não pode vê-lo ou
    /// não pode mexer nele). As duas situações respondem igual de propósito.
    /// </summary>
    public static Error NotFound(PetId id) =>
        new("PET_NOT_FOUND", $"Pet '{id.Value}' was not found.");

    /// <summary>
    /// 404: a conta do token ainda não tem perfil de tutor ou de anfitrião, então não há
    /// a quem ligar o pet.
    /// </summary>
    public static readonly Error KeeperNotFound =
        new("PET_KEEPER_NOT_FOUND", "This account has no owner or host profile yet.");

    /// <summary>
    /// 409: outro pet ativo já tem esse microchip. O número identifica um animal só; a
    /// mensagem orienta quem é o dono de verdade (alguém pode ter digitado o número errado).
    /// </summary>
    public static readonly Error MicrochipAlreadyRegistered =
        new("PET_MICROCHIP_ALREADY_REGISTERED", "Another active pet already has this microchip number. If this pet is yours, contact support.");

    /// <summary>422: o pet já tem o máximo de fotos. Tire uma antes de pôr outra.</summary>
    public static readonly Error PhotoLimitReached =
        new Error("PET_PHOTO_LIMIT_REACHED", $"A pet can have at most {Pet.MaxPhotos} photos. Remove one before adding another.").WithParams(("max", Pet.MaxPhotos));

    /// <summary>409: o pet já tem esta mesma imagem (mesmo arquivo) em uma das fotos.</summary>
    public static readonly Error PhotoAlreadyExists =
        new("PET_PHOTO_ALREADY_EXISTS", "This pet already has this photo.");

    /// <summary>404: o pet não tem foto com esse id.</summary>
    public static Error PhotoNotFound(PetPhotoId id) =>
        new("PET_PHOTO_NOT_FOUND", $"Photo '{id.Value}' was not found.");

    /// <summary>
    /// 422: o pet está numa reserva em aberto (solicitada, aceita, confirmada ou em andamento).
    /// Cancele a estadia antes de desativar.
    /// </summary>
    public static readonly Error HasOpenBooking =
        new("PET_HAS_OPEN_BOOKING", "This pet has an open booking. Cancel the stay before deactivating the pet.");

    /// <summary>404: não existe anfitrião com esse id (ou ele está inativo ou suspenso: a casa não aparece).</summary>
    public static Error HostNotFound(Guid hostId) =>
        new("PET_HOST_NOT_FOUND", $"Host '{hostId}' was not found.");

    // --- Validação: identificadores e campos obrigatórios de comandos internos e de rota ---

    public static readonly Error PetIdRequired =
        Error.Validation("petId", "PET_PET_ID_REQUIRED", "Pet id is required.");

    public static readonly Error KeeperTypeInvalid =
        Error.Validation("keeperType", "PET_KEEPER_TYPE_INVALID", "Keeper type is invalid.");

    public static readonly Error KeeperIdRequired =
        Error.Validation("keeperId", "PET_KEEPER_ID_REQUIRED", "Keeper id is required.");

    public static readonly Error PhotoIdRequired =
        Error.Validation("photoId", "PET_PHOTO_ID_REQUIRED", "Photo id is required.");
}
