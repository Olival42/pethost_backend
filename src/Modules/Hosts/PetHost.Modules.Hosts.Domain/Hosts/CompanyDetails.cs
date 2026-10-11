using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// Os dados da empresa de um anfitrião pessoa jurídica, juntos: CNPJ, razão social, nome
/// fantasia e endereço da empresa. Só existe se tudo for válido — e <see cref="Create"/>
/// devolve <b>todos</b> os erros de uma vez, cada um com o campo do JSON.
/// </summary>
public sealed class CompanyDetails : ValueObject
{
    private CompanyDetails(Cnpj cnpj, string legalName, string tradeName, CompanyAddress address)
    {
        Cnpj = cnpj;
        LegalName = legalName;
        TradeName = tradeName;
        Address = address;
    }

    public Cnpj Cnpj { get; }

    /// <summary>Razão social (Stripe: <c>company.name</c>).</summary>
    public string LegalName { get; }

    /// <summary>Nome fantasia: o nome que o tutor vê.</summary>
    public string TradeName { get; }

    public CompanyAddress Address { get; }

    public static Result<CompanyDetails> Create(
        string? cnpj,
        string? legalName,
        string? tradeName,
        string? zipCode,
        string? street,
        string? number,
        string? complement,
        string? neighborhood,
        string? city,
        string? state)
    {
        var errors = new List<Error>();

        var parsedCnpj = Cnpj.Create(cnpj);
        if (parsedCnpj.IsFailure)
            errors.AddRange(parsedCnpj.Errors!);

        var normalizedLegalName = Required(legalName, Host.LegalNameMaxLength, HostsErrors.LegalNameRequired, HostsErrors.LegalNameTooLong, errors);
        var normalizedTradeName = Required(tradeName, Host.TradeNameMaxLength, HostsErrors.TradeNameRequired, HostsErrors.TradeNameTooLong, errors);

        var address = CompanyAddress.Create(zipCode, street, number, complement, neighborhood, city, state);
        if (address.IsFailure)
            errors.AddRange(address.Errors!);

        if (errors.Count > 0)
            return Result<CompanyDetails>.Failure(errors);

        return Result<CompanyDetails>.Success(
            new CompanyDetails(parsedCnpj.Value!, normalizedLegalName!, normalizedTradeName!, address.Value!));
    }

    /// <summary>Remonta a partir do que já foi validado e gravado (a solicitação no banco).</summary>
    internal static CompanyDetails Restore(Cnpj cnpj, string legalName, string tradeName, CompanyAddress address) =>
        new(cnpj, legalName, tradeName, address);

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
        yield return Cnpj;
        yield return LegalName;
        yield return TradeName;
        yield return Address;
    }
}
