using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Hosts.Application.HostApplications;

/// <summary>
/// Os campos de empresa de um command (abrir ou corrigir a solicitação de pessoa jurídica).
/// Planos no JSON — <c>cnpj</c>, <c>legalName</c>, <c>tradeName</c>, <c>companyAddress</c> —
/// para os erros usarem os mesmos campos do domínio.
/// </summary>
public interface ICompanyFields
{
    string? Cnpj { get; }

    /// <summary>Razão social.</summary>
    string? LegalName { get; }

    /// <summary>Nome fantasia.</summary>
    string? TradeName { get; }

    AddressData? CompanyAddress { get; }
}
