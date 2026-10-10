using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// Endereço da empresa (anfitrião pessoa jurídica), separado do endereço da conta. Vai
/// para o Stripe como <c>company.address</c>; o endereço da conta fica como o do
/// representante. Mesmos campos e tamanhos do endereço da conta.
/// </summary>
public sealed class CompanyAddress : ValueObject
{
    public const int ZipCodeLength = 8;
    public const int StateLength = 2;
    public const int StreetMaxLength = 120;
    public const int NumberMaxLength = 10;
    public const int ComplementMaxLength = 60;
    public const int NeighborhoodMaxLength = 80;
    public const int CityMaxLength = 80;

    private static readonly HashSet<string> ValidStates =
    [
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA",
        "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
    ];

    /// <summary>Construtor só para o EF Core materializar o tipo complexo.</summary>
    private CompanyAddress()
    {
        ZipCode = Street = Number = Neighborhood = City = State = string.Empty;
    }

    private CompanyAddress(
        string zipCode,
        string street,
        string number,
        string? complement,
        string neighborhood,
        string city,
        string state)
    {
        ZipCode = zipCode;
        Street = street;
        Number = number;
        Complement = complement;
        Neighborhood = neighborhood;
        City = city;
        State = state;
    }

    /// <summary>CEP só com dígitos.</summary>
    public string ZipCode { get; private set; }
    public string Street { get; private set; }
    public string Number { get; private set; }
    public string? Complement { get; private set; }
    public string Neighborhood { get; private set; }
    public string City { get; private set; }

    /// <summary>UF em maiúsculas.</summary>
    public string State { get; private set; }

    /// <summary>
    /// Apara tudo e valida campo a campo. Devolve <b>todos</b> os campos inválidos de uma
    /// vez, cada um com o próprio nome (<c>companyAddress.zipCode</c>, ...).
    /// </summary>
    public static Result<CompanyAddress> Create(
        string? zipCode,
        string? street,
        string? number,
        string? complement,
        string? neighborhood,
        string? city,
        string? state)
    {
        var errors = new List<Error>();

        var zipDigits = NormalizeZipCode(zipCode);
        if (zipDigits is null)
            errors.Add(HostsErrors.CompanyZipCodeInvalid);

        var normalizedStreet = Required(street, StreetMaxLength, HostsErrors.CompanyStreetRequired, HostsErrors.CompanyStreetTooLong, errors);
        var normalizedNumber = Required(number, NumberMaxLength, HostsErrors.CompanyStreetNumberRequired, HostsErrors.CompanyStreetNumberTooLong, errors);

        var normalizedComplement = string.IsNullOrWhiteSpace(complement) ? null : complement.Trim();
        if (normalizedComplement?.Length > ComplementMaxLength)
            errors.Add(HostsErrors.CompanyComplementTooLong);

        var normalizedNeighborhood = Required(neighborhood, NeighborhoodMaxLength, HostsErrors.CompanyNeighborhoodRequired, HostsErrors.CompanyNeighborhoodTooLong, errors);
        var normalizedCity = Required(city, CityMaxLength, HostsErrors.CompanyCityRequired, HostsErrors.CompanyCityTooLong, errors);

        var normalizedState = state?.Trim().ToUpperInvariant();
        if (normalizedState is null || !ValidStates.Contains(normalizedState))
            errors.Add(HostsErrors.CompanyStateInvalid);

        if (errors.Count > 0)
            return Result<CompanyAddress>.Failure(errors);

        return Result<CompanyAddress>.Success(new CompanyAddress(
            zipDigits!,
            normalizedStreet!,
            normalizedNumber!,
            normalizedComplement,
            normalizedNeighborhood!,
            normalizedCity!,
            normalizedState!));
    }

    /// <summary>CEP com ou sem máscara → só dígitos; <c>null</c> se inválido.</summary>
    private static string? NormalizeZipCode(string? zipCode)
    {
        if (string.IsNullOrWhiteSpace(zipCode))
            return null;

        var trimmed = zipCode.Trim();
        if (trimmed.Any(c => !char.IsAsciiDigit(c) && c is not ('-' or '.')))
            return null;

        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        return digits.Length == ZipCodeLength && digits != "00000000" ? digits : null;
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
