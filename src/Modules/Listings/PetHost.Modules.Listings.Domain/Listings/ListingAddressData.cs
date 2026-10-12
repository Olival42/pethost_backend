namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// Endereço do cantinho como chega (texto, ainda sem validar). Mesmo formato do endereço da
/// conta: entrada de <see cref="ListingAddress.Create"/>.
/// </summary>
/// <param name="ZipCode">CEP, com ou sem máscara (<c>87020-000</c>).</param>
/// <param name="Number">Texto: aceita "S/N", "120-A".</param>
/// <param name="State">UF, ex.: <c>PR</c>.</param>
public sealed record ListingAddressData(
    string? ZipCode,
    string? Street,
    string? Number,
    string? Complement,
    string? Neighborhood,
    string? City,
    string? State);
