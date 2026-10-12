using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// Endereço do cantinho, validado. Cada filial de uma empresa tem o seu; é dele que sai a
/// coordenada da busca por raio.
/// </summary>
/// <remarks>
/// O bairro e a cidade são o que o tutor vê na busca. Rua, número e complemento só aparecem
/// depois do pagamento (escopo, regra 7): fora o dono, ninguém os recebe.
/// </remarks>
public sealed class ListingAddress : ValueObject
{
    public const int ZipCodeLength = 8;
    public const int StreetMaxLength = 120;
    public const int NumberMaxLength = 10;
    public const int ComplementMaxLength = 60;
    public const int NeighborhoodMaxLength = 80;
    public const int CityMaxLength = 80;
    public const int StateLength = 2;

    private static readonly HashSet<string> States =
    [
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA",
        "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
    ];

    /// <summary>Construtor só para o EF Core materializar o tipo complexo.</summary>
    private ListingAddress()
    {
        ZipCode = string.Empty;
        Street = string.Empty;
        Number = string.Empty;
        Neighborhood = string.Empty;
        City = string.Empty;
        State = string.Empty;
    }

    /// <summary>CEP só com dígitos.</summary>
    public string ZipCode { get; private set; }

    public string Street { get; private set; }

    /// <summary>Texto, não número: existe "S/N", "120-A".</summary>
    public string Number { get; private set; }

    public string? Complement { get; private set; }

    public string Neighborhood { get; private set; }

    public string City { get; private set; }

    /// <summary>UF em maiúsculas.</summary>
    public string State { get; private set; }

    /// <summary>Apara tudo e valida campo a campo. Devolve <b>todos</b> os erros, cada um no campo <c>address.*</c>.</summary>
    public static Result<ListingAddress> Create(ListingAddressData? data)
    {
        data ??= new ListingAddressData(null, null, null, null, null, null, null);

        var errors = new List<Error>();

        var zipCode = CheckZipCode(data.ZipCode, errors);
        var street = Required(data.Street, StreetMaxLength, ListingsErrors.StreetRequired, ListingsErrors.StreetTooLong, errors);
        var number = Required(data.Number, NumberMaxLength, ListingsErrors.NumberRequired, ListingsErrors.NumberTooLong, errors);

        var complement = Normalize(data.Complement);
        if (complement?.Length > ComplementMaxLength)
            errors.Add(ListingsErrors.ComplementTooLong);

        var neighborhood = Required(data.Neighborhood, NeighborhoodMaxLength, ListingsErrors.NeighborhoodRequired, ListingsErrors.NeighborhoodTooLong, errors);
        var city = Required(data.City, CityMaxLength, ListingsErrors.CityRequired, ListingsErrors.CityTooLong, errors);

        var state = data.State?.Trim().ToUpperInvariant();
        if (state is null || !States.Contains(state))
            errors.Add(ListingsErrors.StateInvalid);

        if (errors.Count > 0)
            return Result<ListingAddress>.Failure(errors);

        return Result<ListingAddress>.Success(new ListingAddress
        {
            ZipCode = zipCode ?? string.Empty,
            Street = street ?? string.Empty,
            Number = number ?? string.Empty,
            Complement = complement,
            Neighborhood = neighborhood ?? string.Empty,
            City = city ?? string.Empty,
            State = state ?? string.Empty,
        });
    }

    /// <summary>O endereço de volta no formato de entrada: base para mesclar um PATCH.</summary>
    public ListingAddressData ToData() => new(ZipCode, Street, Number, Complement, Neighborhood, City, State);

    /// <summary>Aceita máscara (<c>87020-000</c>, <c>87.020-000</c>); guarda só os 8 dígitos.</summary>
    private static string? CheckZipCode(string? value, List<Error> errors)
    {
        var text = Normalize(value);
        var onlyDigitsAndMask = text is not null && text.All(c => char.IsAsciiDigit(c) || c is '-' or '.');
        var digits = text is null ? string.Empty : new string([.. text.Where(char.IsAsciiDigit)]);

        if (!onlyDigitsAndMask || digits.Length != ZipCodeLength || digits == "00000000")
        {
            errors.Add(ListingsErrors.ZipCodeInvalid);
            return null;
        }

        return digits;
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

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ZipCode;
        yield return Street;
        yield return Number;
        yield return Complement;
        yield return Neighborhood;
        yield return City;
        yield return State;
    }
}
