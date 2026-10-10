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
        Error.Validation("species", "Species is required.");

    public static readonly Error SpeciesInvalid =
        Error.Validation("species", $"Species must be one of: {string.Join(", ", PetSpeciesValues.All)}.");

    public static readonly Error SpeciesDescriptionRequired =
        Error.Validation("speciesDescription", "Describe the animal when the species is 'exotic'.");

    public static readonly Error SpeciesDescriptionTooLong =
        Error.Validation("speciesDescription", $"Species description must be at most {PetProfile.SpeciesDescriptionMaxLength} characters.");

    public static readonly Error SpeciesDescriptionNotAllowed =
        Error.Validation("speciesDescription", "Species description is only for exotic animals.");

    public static readonly Error NameRequired =
        Error.Validation("name", "Name is required.");

    public static readonly Error NameTooLong =
        Error.Validation("name", $"Name must be at most {PetProfile.NameMaxLength} characters.");

    // A URL da foto vem do bucket, não do cliente: estes dois só aparecem com o storage mal configurado.
    public static readonly Error PhotoUrlInvalid =
        Error.Validation("photoUrl", "Photo URL must be an absolute http or https address.");

    public static readonly Error PhotoUrlTooLong =
        Error.Validation("photoUrl", $"Photo URL must be at most {PetPhoto.UrlMaxLength} characters.");

    public static readonly Error BreedTooLong =
        Error.Validation("breed", $"Breed must be at most {PetProfile.BreedMaxLength} characters.");

    public static readonly Error SizeRequired =
        Error.Validation("size", "Size is required for dogs.");

    public static readonly Error SizeInvalid =
        Error.Validation("size", $"Size must be one of: {string.Join(", ", PetSizeValues.All)}.");

    public static readonly Error SizeNotAllowed =
        Error.Validation("size", "Size only applies to dogs and cats.");

    public static readonly Error BirthDateInvalidFormat =
        Error.Validation("birthDate", "Birth date must be a valid date in the format yyyy-MM-dd.");

    public static readonly Error BirthDateInFuture =
        Error.Validation("birthDate", "Birth date cannot be in the future.");

    public static readonly Error SexRequired =
        Error.Validation("sex", "Sex is required.");

    public static readonly Error SexInvalid =
        Error.Validation("sex", $"Sex must be one of: {string.Join(", ", PetSexValues.All)}.");

    public static readonly Error IsNeuteredRequired =
        Error.Validation("isNeutered", "Inform whether the pet is neutered.");

    public static readonly Error IsVaccinatedRequired =
        Error.Validation("isVaccinated", "Inform whether the pet's vaccines are up to date.");

    public static readonly Error GoodWithDogsRequired =
        Error.Validation("goodWithDogs", "Inform whether the pet gets along with dogs.");

    public static readonly Error GoodWithCatsRequired =
        Error.Validation("goodWithCats", "Inform whether the pet gets along with cats.");

    public static readonly Error GoodWithKidsRequired =
        Error.Validation("goodWithKids", "Inform whether the pet gets along with kids.");

    public static readonly Error MedicationNotesTooLong =
        Error.Validation("medicationNotes", $"Medication notes must be at most {PetProfile.CareNotesMaxLength} characters.");

    public static readonly Error FeedingNotesTooLong =
        Error.Validation("feedingNotes", $"Feeding notes must be at most {PetProfile.CareNotesMaxLength} characters.");

    public static readonly Error VetContactTooLong =
        Error.Validation("vetContact", $"Vet contact must be at most {PetProfile.VetContactMaxLength} characters.");

    public static readonly Error NotesTooLong =
        Error.Validation("notes", $"Notes must be at most {PetProfile.NotesMaxLength} characters.");

    public static readonly Error WeightOutOfRange =
        Error.Validation("weightKg", $"Weight must be greater than 0 and at most {PetProfile.WeightMaxKg} kg.");

    public static readonly Error MicrochipInvalid =
        Error.Validation("microchip", $"Microchip number must have {PetProfile.MicrochipLength} digits.");

    public static readonly Error AllergiesTooLong =
        Error.Validation("allergies", $"Allergies must be at most {PetProfile.CareNotesMaxLength} characters.");

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
        new("PET_PHOTO_LIMIT_REACHED", $"A pet can have at most {Pet.MaxPhotos} photos. Remove one before adding another.");

    /// <summary>409: o pet já tem esta mesma imagem (mesmo arquivo) em uma das fotos.</summary>
    public static readonly Error PhotoAlreadyExists =
        new("PET_PHOTO_ALREADY_EXISTS", "This pet already has this photo.");

    /// <summary>404: o pet não tem foto com esse id.</summary>
    public static Error PhotoNotFound(PetPhotoId id) =>
        new("PET_PHOTO_NOT_FOUND", $"Photo '{id.Value}' was not found.");

    /// <summary>404: não existe anfitrião com esse id.</summary>
    public static Error HostNotFound(Guid hostId) =>
        new("PET_HOST_NOT_FOUND", $"Host '{hostId}' was not found.");
}
