using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Hosts.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Hosts. Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class HostsErrors
{
    // --- Validação de formato (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error CpfInvalid =
        Error.Validation("cpf", "CPF is invalid.");

    public static readonly Error CnpjInvalid =
        Error.Validation("cnpj", "CNPJ is invalid.");

    public static readonly Error LegalNameRequired =
        Error.Validation("legalName", "Legal name is required for a company.");

    public static readonly Error LegalNameTooLong =
        Error.Validation("legalName", $"Legal name must be at most {Host.LegalNameMaxLength} characters.");

    public static readonly Error TradeNameRequired =
        Error.Validation("tradeName", "Trade name is required for a company.");

    public static readonly Error TradeNameTooLong =
        Error.Validation("tradeName", $"Trade name must be at most {Host.TradeNameMaxLength} characters.");

    // --- Endereço da empresa (o campo leva o prefixo "companyAddress." porque o JSON é aninhado) ---

    public static readonly Error CompanyZipCodeInvalid =
        Error.Validation("companyAddress.zipCode", $"Zip code (CEP) must have {CompanyAddress.ZipCodeLength} digits.");

    public static readonly Error CompanyStreetRequired =
        Error.Validation("companyAddress.street", "Street is required.");

    public static readonly Error CompanyStreetTooLong =
        Error.Validation("companyAddress.street", $"Street must be at most {CompanyAddress.StreetMaxLength} characters.");

    public static readonly Error CompanyStreetNumberRequired =
        Error.Validation("companyAddress.number", "Number is required. Use 'S/N' when there is none.");

    public static readonly Error CompanyStreetNumberTooLong =
        Error.Validation("companyAddress.number", $"Number must be at most {CompanyAddress.NumberMaxLength} characters.");

    public static readonly Error CompanyComplementTooLong =
        Error.Validation("companyAddress.complement", $"Complement must be at most {CompanyAddress.ComplementMaxLength} characters.");

    public static readonly Error CompanyNeighborhoodRequired =
        Error.Validation("companyAddress.neighborhood", "Neighborhood is required.");

    public static readonly Error CompanyNeighborhoodTooLong =
        Error.Validation("companyAddress.neighborhood", $"Neighborhood must be at most {CompanyAddress.NeighborhoodMaxLength} characters.");

    public static readonly Error CompanyCityRequired =
        Error.Validation("companyAddress.city", "City is required.");

    public static readonly Error CompanyCityTooLong =
        Error.Validation("companyAddress.city", $"City must be at most {CompanyAddress.CityMaxLength} characters.");

    public static readonly Error CompanyStateInvalid =
        Error.Validation("companyAddress.state", "State must be a valid Brazilian state code, e.g. 'PR'.");
}
