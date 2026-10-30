using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Endereço brasileiro completo. Os campos batem com o que o Stripe pede no
/// endereço de cobrança: <c>line1</c> = rua + número, <c>line2</c> = complemento,
/// <c>postal_code</c>, <c>city</c>, <c>state</c>, <c>country = BR</c>.
/// </summary>
/// <remarks>
/// O bairro é o que aparece para os outros (selo "Vizinho", busca). Rua, número e
/// complemento só são mostrados depois do pagamento (escopo, regra 7).
/// </remarks>
public sealed class Address : ValueObject
{
    public const int StreetMaxLength = 120;
    public const int NumberMaxLength = 10;
    public const int ComplementMaxLength = 60;
    public const int NeighborhoodMaxLength = 80;
    public const int CityMaxLength = 80;

    /// <summary>Construtor só para o EF Core materializar o tipo complexo.</summary>
    private Address()
    {
        ZipCode = null!;
        Street = null!;
        Number = null!;
        Neighborhood = null!;
        City = null!;
        State = null!;
    }

    private Address(
        ZipCode zipCode,
        string street,
        string number,
        string? complement,
        string neighborhood,
        string city,
        StateCode state)
    {
        ZipCode = zipCode;
        Street = street;
        Number = number;
        Complement = complement;
        Neighborhood = neighborhood;
        City = city;
        State = state;
    }

    public ZipCode ZipCode { get; private set; }
    public string Street { get; private set; }

    /// <summary>Texto, não número: existe "S/N", "120-A", "Km 12".</summary>
    public string Number { get; private set; }

    public string? Complement { get; private set; }
    public string Neighborhood { get; private set; }
    public string City { get; private set; }
    public StateCode State { get; private set; }

    /// <summary>
    /// Apara tudo e valida campo a campo. Devolve <b>todos</b> os campos inválidos
    /// de uma vez, cada um com o próprio nome (<c>address.zipCode</c>, ...).
    /// </summary>
    public static Result<Address> Create(
        string? zipCode,
        string? street,
        string? number,
        string? complement,
        string? neighborhood,
        string? city,
        string? state)
    {
        var errors = new List<Error>();

        var zip = ZipCode.Create(zipCode);
        if (zip.IsFailure)
            errors.AddRange(zip.Errors!);

        var normalizedStreet = Required(street, StreetMaxLength, AuthErrors.StreetRequired, AuthErrors.StreetTooLong, errors);
        var normalizedNumber = Required(number, NumberMaxLength, AuthErrors.StreetNumberRequired, AuthErrors.StreetNumberTooLong, errors);

        var normalizedComplement = string.IsNullOrWhiteSpace(complement) ? null : complement.Trim();
        if (normalizedComplement?.Length > ComplementMaxLength)
            errors.Add(AuthErrors.ComplementTooLong);

        var normalizedNeighborhood = Required(neighborhood, NeighborhoodMaxLength, AuthErrors.NeighborhoodRequired, AuthErrors.NeighborhoodTooLong, errors);
        var normalizedCity = Required(city, CityMaxLength, AuthErrors.CityRequired, AuthErrors.CityTooLong, errors);

        var stateCode = StateCode.Create(state);
        if (stateCode.IsFailure)
            errors.Add(AuthErrors.AddressStateInvalid);

        if (errors.Count > 0)
            return Result<Address>.Failure(errors);

        return Result<Address>.Success(new Address(
            zip.Value!,
            normalizedStreet!,
            normalizedNumber!,
            normalizedComplement,
            normalizedNeighborhood!,
            normalizedCity!,
            stateCode.Value!));
    }

    private static string? Required(string? value, int maxLength, Error required, Error tooLong, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(required);
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            errors.Add(tooLong);

        return trimmed;
    }

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
