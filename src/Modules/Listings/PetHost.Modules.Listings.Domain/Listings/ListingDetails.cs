using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// O cantinho descrito pelo anfitrião, já validado: a casa, quem mora nela, o que aceita, a
/// capacidade, a diária e os dias da semana em que recebe. Valor sem identidade — trocar é
/// trocar o objeto inteiro.
/// </summary>
/// <remarks>
/// Regras entre campos: quem aceita cachorro ou gato precisa aceitar ao menos um porte; o tipo
/// da casa é obrigatório só para pessoa física; empresa tem teto de capacidade maior. Texto vazio num campo opcional vira <c>null</c>.
/// </remarks>
public sealed class ListingDetails : ValueObject
{
    public const int TitleMaxLength = 80;
    public const int DescriptionMaxLength = 4000;
    public const int HouseRulesMaxLength = 2000;

    /// <summary>Teto da capacidade de pessoa física: um cantinho é uma casa, não um canil.</summary>
    public const int MaxCapacityIndividual = 10;

    /// <summary>Teto da capacidade de empresa, por filial: hotel, creche e resort de pets comportam mais.</summary>
    public const int MaxCapacityCompany = 200;

    /// <summary>Menor diária aceita, em reais.</summary>
    public const decimal MinNightlyRate = 10m;

    /// <summary>Maior diária aceita, em reais.</summary>
    public const decimal MaxNightlyRate = 10_000m;

    /// <summary>Casas decimais da diária: centavos.</summary>
    public const int NightlyRateDecimals = 2;

    /// <summary>Construtor só para o EF Core materializar o tipo complexo.</summary>
    private ListingDetails()
    {
        Title = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Ex.: "Casa com quintal no Jardim Alvorada".</summary>
    public string Title { get; private set; }

    /// <summary>Descrição livre da casa e da rotina.</summary>
    public string Description { get; private set; }

    /// <summary>Casa ou apartamento. Obrigatório para pessoa física; opcional para empresa (filial é espaço comercial).</summary>
    public HomeType? HomeType { get; private set; }

    /// <summary>Tem quintal. Gera o selo "Casa com quintal".</summary>
    public bool HasYard { get; private set; }

    /// <summary>Tem cães em casa. Usado no "Combina com seu pet".</summary>
    public bool HasDogs { get; private set; }

    public bool HasCats { get; private set; }

    public bool HasKids { get; private set; }

    /// <summary>Tipos de pet aceitos. Nunca vazio.</summary>
    public AcceptedSpecies AcceptedSpecies { get; private set; }

    /// <summary>Aceita porte pequeno (vale para cachorro e gato).</summary>
    public bool AcceptsSmall { get; private set; }

    public bool AcceptsMedium { get; private set; }

    public bool AcceptsLarge { get; private set; }

    /// <summary>
    /// Quantos pets ao mesmo tempo, somando todos os tutores. De 1 a <see cref="MaxCapacityIndividual"/>
    /// (pessoa física) ou <see cref="MaxCapacityCompany"/> (empresa).
    /// </summary>
    public int Capacity { get; private set; }

    /// <summary>Diária por pet, em reais, com 2 casas.</summary>
    public decimal NightlyRate { get; private set; }

    public string? HouseRules { get; private set; }

    /// <summary>Dias da semana em que recebe pets. Nunca vazio.</summary>
    public Weekdays AvailableWeekdays { get; private set; }

    /// <summary>Valida tudo e devolve <b>todos</b> os erros de uma vez, cada um com o campo do JSON.</summary>
    /// <param name="isCompany">O dono é empresa: o tipo da casa deixa de ser obrigatório e a capacidade vai até <see cref="MaxCapacityCompany"/>.</param>
    public static Result<ListingDetails> Create(ListingDetailsData data, bool isCompany)
    {
        ArgumentNullException.ThrowIfNull(data);

        var errors = new List<Error>();

        var title = Required(data.Title, TitleMaxLength, ListingsErrors.TitleRequired, ListingsErrors.TitleTooLong, errors);
        var description = Required(data.Description, DescriptionMaxLength, ListingsErrors.DescriptionRequired, ListingsErrors.DescriptionTooLong, errors);
        var homeType = ParseHomeType(data.HomeType, isCompany, errors);
        var hasYard = RequiredFlag(data.HasYard, ListingsErrors.HasYardRequired, errors);
        var hasDogs = RequiredFlag(data.HasDogs, ListingsErrors.HasDogsRequired, errors);
        var hasCats = RequiredFlag(data.HasCats, ListingsErrors.HasCatsRequired, errors);
        var hasKids = RequiredFlag(data.HasKids, ListingsErrors.HasKidsRequired, errors);
        var species = ParseSpecies(data.AcceptedSpecies, errors);
        var acceptsSmall = RequiredFlag(data.AcceptsSmall, ListingsErrors.AcceptsSmallRequired, errors);
        var acceptsMedium = RequiredFlag(data.AcceptsMedium, ListingsErrors.AcceptsMediumRequired, errors);
        var acceptsLarge = RequiredFlag(data.AcceptsLarge, ListingsErrors.AcceptsLargeRequired, errors);
        var capacity = CheckCapacity(data.Capacity, isCompany, errors);
        var nightlyRate = CheckNightlyRate(data.NightlyRate, errors);
        var houseRules = Optional(data.HouseRules, HouseRulesMaxLength, ListingsErrors.HouseRulesTooLong, errors);
        var weekdays = ParseWeekdays(data.AvailableWeekdays, errors);

        // Só confere o porte quando os três vieram: senão o erro de "obrigatório" já basta.
        var sizesInformed = data.AcceptsSmall is not null && data.AcceptsMedium is not null && data.AcceptsLarge is not null;
        if (sizesInformed
            && (species & (AcceptedSpecies.Dog | AcceptedSpecies.Cat)) != AcceptedSpecies.None
            && !acceptsSmall && !acceptsMedium && !acceptsLarge)
        {
            errors.Add(ListingsErrors.SizeRequiredForDogsAndCats);
        }

        if (errors.Count > 0)
            return Result<ListingDetails>.Failure(errors);

        return Result<ListingDetails>.Success(new ListingDetails
        {
            Title = title ?? string.Empty,
            Description = description ?? string.Empty,
            HomeType = homeType,
            HasYard = hasYard,
            HasDogs = hasDogs,
            HasCats = hasCats,
            HasKids = hasKids,
            AcceptedSpecies = species,
            AcceptsSmall = acceptsSmall,
            AcceptsMedium = acceptsMedium,
            AcceptsLarge = acceptsLarge,
            Capacity = capacity,
            NightlyRate = nightlyRate,
            HouseRules = houseRules,
            AvailableWeekdays = weekdays,
        });
    }

    /// <summary>De volta no formato de entrada: base para mesclar um PATCH.</summary>
    public ListingDetailsData ToData() =>
        new(
            Title,
            Description,
            HomeType is { } homeType ? HomeTypeValues.ToWire(homeType) : null,
            HasYard,
            HasDogs,
            HasCats,
            HasKids,
            [.. AcceptedSpeciesValues.ToWire(AcceptedSpecies)],
            AcceptsSmall,
            AcceptsMedium,
            AcceptsLarge,
            Capacity,
            NightlyRate,
            HouseRules,
            [.. WeekdaysValues.ToWire(AvailableWeekdays)]);

    /// <summary>Obrigatório para pessoa física; empresa pode deixar em branco.</summary>
    private static HomeType? ParseHomeType(string? value, bool isCompany, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!isCompany)
                errors.Add(ListingsErrors.HomeTypeRequired);

            return null;
        }

        if (HomeTypeValues.TryParse(value, out var homeType))
            return homeType;

        errors.Add(ListingsErrors.HomeTypeInvalid);
        return null;
    }

    private static AcceptedSpecies ParseSpecies(IReadOnlyList<string?>? values, List<Error> errors)
    {
        if (values is null || values.Count == 0)
        {
            errors.Add(ListingsErrors.AcceptedSpeciesRequired);
            return AcceptedSpecies.None;
        }

        if (AcceptedSpeciesValues.TryParse(values, out var species))
            return species;

        errors.Add(ListingsErrors.AcceptedSpeciesInvalid);
        return AcceptedSpecies.None;
    }

    private static Weekdays ParseWeekdays(IReadOnlyList<string?>? values, List<Error> errors)
    {
        if (values is null || values.Count == 0)
        {
            errors.Add(ListingsErrors.AvailableWeekdaysRequired);
            return Weekdays.None;
        }

        if (WeekdaysValues.TryParse(values, out var days))
            return days;

        errors.Add(ListingsErrors.AvailableWeekdaysInvalid);
        return Weekdays.None;
    }

    /// <summary>Teto da capacidade conforme o dono.</summary>
    public static int MaxCapacityFor(bool isCompany) => isCompany ? MaxCapacityCompany : MaxCapacityIndividual;

    private static int CheckCapacity(int? value, bool isCompany, List<Error> errors)
    {
        if (value is null)
        {
            errors.Add(ListingsErrors.CapacityRequired);
            return 0;
        }

        if (value < 1 || value > MaxCapacityFor(isCompany))
            errors.Add(ListingsErrors.CapacityOutOfRange(isCompany));

        return value.Value;
    }

    /// <summary>Na faixa e com no máximo 2 casas: 60.5 vale, 60.555 não (não se cobra fração de centavo).</summary>
    private static decimal CheckNightlyRate(decimal? value, List<Error> errors)
    {
        if (value is null)
        {
            errors.Add(ListingsErrors.NightlyRateRequired);
            return 0m;
        }

        if (value is < MinNightlyRate or > MaxNightlyRate)
            errors.Add(ListingsErrors.NightlyRateOutOfRange);
        else if (value.Value != Math.Round(value.Value, NightlyRateDecimals))
            errors.Add(ListingsErrors.NightlyRateTooPrecise);

        return value.Value;
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
        yield return Title;
        yield return Description;
        yield return HomeType;
        yield return HasYard;
        yield return HasDogs;
        yield return HasCats;
        yield return HasKids;
        yield return AcceptedSpecies;
        yield return AcceptsSmall;
        yield return AcceptsMedium;
        yield return AcceptsLarge;
        yield return Capacity;
        yield return NightlyRate;
        yield return HouseRules;
        yield return AvailableWeekdays;
    }
}
