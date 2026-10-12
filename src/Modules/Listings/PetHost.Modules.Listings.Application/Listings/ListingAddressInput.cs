using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Listings;

/// <summary>
/// Endereço do cantinho como vem no comando (texto, sem validar). Existe para o controller não
/// conhecer o domínio (§4): o handler converte com <see cref="ToData"/>.
/// </summary>
/// <param name="ZipCode">CEP, com ou sem máscara.</param>
/// <param name="Number">Aceita "S/N".</param>
/// <param name="State">UF, ex.: <c>PR</c>.</param>
public sealed record ListingAddressInput(
    string? ZipCode,
    string? Street,
    string? Number,
    string? Complement,
    string? Neighborhood,
    string? City,
    string? State)
{
    public ListingAddressData ToData() => new(ZipCode, Street, Number, Complement, Neighborhood, City, State);
}
