using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Hosts.Application.HostApplications.Responses;

/// <summary>Os dados da empresa (anfitrião pessoa jurídica). Ausente na pessoa física.</summary>
/// <param name="Cnpj">Sem máscara, em maiúsculas (aceita o CNPJ alfanumérico).</param>
/// <param name="LegalName">Razão social.</param>
/// <param name="TradeName">Nome fantasia: o nome que o tutor vê.</param>
public sealed record CompanyResponse(string Cnpj, string LegalName, string TradeName, AddressData Address)
{
    public static CompanyResponse? From(CompanyDetails? company) =>
        company is null
            ? null
            : new CompanyResponse(
                company.Cnpj.Value,
                company.LegalName,
                company.TradeName,
                new AddressData(
                    company.Address.ZipCode,
                    company.Address.Street,
                    company.Address.Number,
                    company.Address.Complement,
                    company.Address.Neighborhood,
                    company.Address.City,
                    company.Address.State));
}
