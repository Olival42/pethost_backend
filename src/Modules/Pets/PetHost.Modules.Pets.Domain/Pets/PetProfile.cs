using System.Globalization;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>
/// A ficha do pet já validada: tudo que o anfitrião lê antes de aceitar a estadia. Valor
/// sem identidade — trocar a ficha é trocar o objeto inteiro.
/// </summary>
/// <remarks>
/// Regras entre campos: porte é obrigatório no cachorro, opcional no gato e proibido nos
/// outros; a descrição do animal só existe (e é obrigatória) para
/// <see cref="PetSpecies.Exotic"/>. Texto vazio num campo opcional vira <c>null</c>.
/// </remarks>
public sealed class PetProfile : ValueObject
{
    public const int NameMaxLength = 60;
    public const int SpeciesDescriptionMaxLength = 60;
    public const int BreedMaxLength = 60;
    public const int VetContactMaxLength = 160;

    /// <summary>Limite de remédio e de alimentação: o bastante para dose, horário e rotina.</summary>
    public const int CareNotesMaxLength = 1000;

    /// <summary>Limite de "coisas que só quem convive sabe".</summary>
    public const int NotesMaxLength = 2000;

    /// <summary>Teto do peso, com folga sobre os maiores cães.</summary>
    public const decimal WeightMaxKg = 150m;

    /// <summary>Casas decimais do peso: gramas, para hamster e passarinho.</summary>
    public const int WeightDecimals = 3;

    /// <summary>Microchip ISO 11784/11785: 15 dígitos.</summary>
    public const int MicrochipLength = 15;

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Construtor só para o EF Core materializar o tipo complexo.</summary>
    private PetProfile()
    {
        Name = string.Empty;
    }

    public PetSpecies Species { get; private set; }

    /// <summary>Só em <see cref="PetSpecies.Exotic"/>.</summary>
    public string? SpeciesDescription { get; private set; }

    public string Name { get; private set; }

    /// <summary>Texto livre. Vazio = sem raça definida.</summary>
    public string? Breed { get; private set; }

    /// <summary>Só cachorro.</summary>
    public PetSize? Size { get; private set; }

    /// <summary>Nascimento aproximado; a idade é calculada.</summary>
    public DateOnly? BirthDate { get; private set; }

    public PetSex Sex { get; private set; }

    public bool IsNeutered { get; private set; }

    /// <summary>Vacinas em dia.</summary>
    public bool IsVaccinated { get; private set; }

    /// <summary>Remédio, dose e horário.</summary>
    public string? MedicationNotes { get; private set; }

    /// <summary>Rotina de alimentação. A ração é sempre levada pelo tutor.</summary>
    public string? FeedingNotes { get; private set; }

    public bool GoodWithDogs { get; private set; }
    public bool GoodWithCats { get; private set; }
    public bool GoodWithKids { get; private set; }

    /// <summary>Veterinário de confiança (nome e telefone).</summary>
    public string? VetContact { get; private set; }

    /// <summary>"Coisas que só quem convive sabe": medos, manias, comandos.</summary>
    public string? Notes { get; private set; }

    /// <summary>Peso aproximado em kg, até 3 casas. Ajuda onde não há porte (coelho, ave).</summary>
    public decimal? WeightKg { get; private set; }

    /// <summary>Número do microchip, só dígitos.</summary>
    public string? Microchip { get; private set; }

    /// <summary>Alergias e restrições: alimentos, remédios, produtos.</summary>
    public string? Allergies { get; private set; }

    /// <summary>
    /// Valida a ficha inteira e devolve <b>todos</b> os erros de uma vez, cada um com o
    /// campo do JSON.
    /// </summary>
    public static Result<PetProfile> Create(PetProfileData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);

        var errors = new List<Error>();

        var species = ParseSpecies(data.Species, errors);
        var speciesDescription = CheckSpeciesDescription(species, data.SpeciesDescription, errors);
        var name = Required(data.Name, NameMaxLength, PetsErrors.NameRequired, PetsErrors.NameTooLong, errors);
        var breed = Optional(data.Breed, BreedMaxLength, PetsErrors.BreedTooLong, errors);
        var size = CheckSize(species, data.Size, errors);
        var birthDate = CheckBirthDate(data.BirthDate, now, errors);
        var sex = ParseSex(data.Sex, errors);
        var isNeutered = RequiredFlag(data.IsNeutered, PetsErrors.IsNeuteredRequired, errors);
        var isVaccinated = RequiredFlag(data.IsVaccinated, PetsErrors.IsVaccinatedRequired, errors);
        var medicationNotes = Optional(data.MedicationNotes, CareNotesMaxLength, PetsErrors.MedicationNotesTooLong, errors);
        var feedingNotes = Optional(data.FeedingNotes, CareNotesMaxLength, PetsErrors.FeedingNotesTooLong, errors);
        var goodWithDogs = RequiredFlag(data.GoodWithDogs, PetsErrors.GoodWithDogsRequired, errors);
        var goodWithCats = RequiredFlag(data.GoodWithCats, PetsErrors.GoodWithCatsRequired, errors);
        var goodWithKids = RequiredFlag(data.GoodWithKids, PetsErrors.GoodWithKidsRequired, errors);
        var vetContact = Optional(data.VetContact, VetContactMaxLength, PetsErrors.VetContactTooLong, errors);
        var notes = Optional(data.Notes, NotesMaxLength, PetsErrors.NotesTooLong, errors);
        var weightKg = CheckWeight(data.WeightKg, errors);
        var microchip = CheckMicrochip(data.Microchip, errors);
        var allergies = Optional(data.Allergies, CareNotesMaxLength, PetsErrors.AllergiesTooLong, errors);

        if (errors.Count > 0)
            return Result<PetProfile>.Failure(errors);

        return Result<PetProfile>.Success(new PetProfile
        {
            Species = species.GetValueOrDefault(),
            SpeciesDescription = speciesDescription,
            Name = name ?? string.Empty,
            Breed = breed,
            Size = size,
            BirthDate = birthDate,
            Sex = sex.GetValueOrDefault(),
            IsNeutered = isNeutered,
            IsVaccinated = isVaccinated,
            MedicationNotes = medicationNotes,
            FeedingNotes = feedingNotes,
            GoodWithDogs = goodWithDogs,
            GoodWithCats = goodWithCats,
            GoodWithKids = goodWithKids,
            VetContact = vetContact,
            Notes = notes,
            WeightKg = weightKg,
            Microchip = microchip,
            Allergies = allergies,
        });
    }

    /// <summary>A ficha de volta no formato de entrada: base para mesclar um PATCH.</summary>
    public PetProfileData ToData() =>
        new(
            PetSpeciesValues.ToWire(Species),
            SpeciesDescription,
            Name,
            Breed,
            Size is { } size ? PetSizeValues.ToWire(size) : null,
            BirthDate?.ToString(DateFormat, CultureInfo.InvariantCulture),
            PetSexValues.ToWire(Sex),
            IsNeutered,
            IsVaccinated,
            MedicationNotes,
            FeedingNotes,
            GoodWithDogs,
            GoodWithCats,
            GoodWithKids,
            VetContact,
            Notes,
            WeightKg,
            Microchip,
            Allergies);

    /// <summary>
    /// Maior que zero e até o teto, arredondado para gramas. Sem peso é sem peso: nulo.
    /// </summary>
    private static decimal? CheckWeight(decimal? value, List<Error> errors)
    {
        if (value is null)
            return null;

        if (value <= 0 || value > WeightMaxKg)
        {
            errors.Add(PetsErrors.WeightOutOfRange);
            return null;
        }

        return Math.Round(value.Value, WeightDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>Aceita espaços e hífens entre os dígitos (como vem na carteirinha); guarda só os dígitos.</summary>
    private static string? CheckMicrochip(string? value, List<Error> errors)
    {
        var text = Normalize(value);
        if (text is null)
            return null;

        var onlyDigitsAndSeparators = text.All(c => char.IsAsciiDigit(c) || c is ' ' or '-');
        var digits = new string([.. text.Where(char.IsAsciiDigit)]);

        if (!onlyDigitsAndSeparators || digits.Length != MicrochipLength)
        {
            errors.Add(PetsErrors.MicrochipInvalid);
            return null;
        }

        return digits;
    }

    private static PetSpecies? ParseSpecies(string? value, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(PetsErrors.SpeciesRequired);
            return null;
        }

        if (PetSpeciesValues.TryParse(value, out var species))
            return species;

        errors.Add(PetsErrors.SpeciesInvalid);
        return null;
    }

    /// <summary>Obrigatória no exótico; proibida nos outros (a espécie já diz o que é).</summary>
    private static string? CheckSpeciesDescription(PetSpecies? species, string? value, List<Error> errors)
    {
        var description = Normalize(value);

        if (species is PetSpecies.Exotic)
        {
            if (description is null)
                errors.Add(PetsErrors.SpeciesDescriptionRequired);
            else if (description.Length > SpeciesDescriptionMaxLength)
                errors.Add(PetsErrors.SpeciesDescriptionTooLong);

            return description;
        }

        if (species is not null && description is not null)
            errors.Add(PetsErrors.SpeciesDescriptionNotAllowed);

        return null;
    }

    /// <summary>A espécie informa porte? Cachorro (obrigatório) e gato (opcional).</summary>
    public static bool HasSize(PetSpecies species) => species is PetSpecies.Dog or PetSpecies.Cat;

    /// <summary>Obrigatório no cachorro; opcional no gato; proibido nos outros.</summary>
    private static PetSize? CheckSize(PetSpecies? species, string? value, List<Error> errors)
    {
        var text = Normalize(value);

        if (species is { } known && HasSize(known))
        {
            if (text is null)
            {
                if (known is PetSpecies.Dog)
                    errors.Add(PetsErrors.SizeRequired);

                return null;
            }

            if (PetSizeValues.TryParse(text, out var size))
                return size;

            errors.Add(PetsErrors.SizeInvalid);
            return null;
        }

        if (species is not null && text is not null)
            errors.Add(PetsErrors.SizeNotAllowed);

        return null;
    }

    private static DateOnly? CheckBirthDate(string? value, DateTimeOffset now, List<Error> errors)
    {
        var text = Normalize(value);
        if (text is null)
            return null;

        if (!DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add(PetsErrors.BirthDateInvalidFormat);
            return null;
        }

        if (date > DateOnly.FromDateTime(now.UtcDateTime))
            errors.Add(PetsErrors.BirthDateInFuture);

        return date;
    }

    private static PetSex? ParseSex(string? value, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(PetsErrors.SexRequired);
            return null;
        }

        if (PetSexValues.TryParse(value, out var sex))
            return sex;

        errors.Add(PetsErrors.SexInvalid);
        return null;
    }

    private static bool RequiredFlag(bool? value, Error required, List<Error> errors)
    {
        if (value is null)
            errors.Add(required);

        return value ?? false;
    }

    private static string? Required(string? value, int maxLength, Error required, Error tooLong, List<Error> errors)
    {
        var text = Normalize(value);
        if (text is null)
        {
            errors.Add(required);
            return null;
        }

        if (text.Length > maxLength)
            errors.Add(tooLong);

        return text;
    }

    private static string? Optional(string? value, int maxLength, Error tooLong, List<Error> errors)
    {
        var text = Normalize(value);
        if (text?.Length > maxLength)
            errors.Add(tooLong);

        return text;
    }

    /// <summary>Apara; texto vazio vira <c>null</c> (é assim que o PATCH limpa um opcional).</summary>
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Species;
        yield return SpeciesDescription;
        yield return Name;
        yield return Breed;
        yield return Size;
        yield return BirthDate;
        yield return Sex;
        yield return IsNeutered;
        yield return IsVaccinated;
        yield return MedicationNotes;
        yield return FeedingNotes;
        yield return GoodWithDogs;
        yield return GoodWithCats;
        yield return GoodWithKids;
        yield return VetContact;
        yield return Notes;
        yield return WeightKg;
        yield return Microchip;
        yield return Allergies;
    }
}
