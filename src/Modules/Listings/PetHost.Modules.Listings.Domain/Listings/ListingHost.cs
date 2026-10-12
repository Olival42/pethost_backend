namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// O anfitrião dono do cantinho, como o módulo Listings o enxerga: o id (<c>host.hosts.id</c>,
/// nunca o da conta) e se é empresa — só empresa tem mais de um cantinho (uma por filial).
/// </summary>
/// <param name="Id">Id do anfitrião.</param>
/// <param name="IsCompany">Pessoa jurídica.</param>
public readonly record struct ListingHost(Guid Id, bool IsCompany);
