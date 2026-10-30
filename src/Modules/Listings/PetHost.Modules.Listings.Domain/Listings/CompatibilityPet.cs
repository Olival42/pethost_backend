namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// O que o "Combina com seu pet" precisa da ficha de um pet (do tutor que vê o cantinho ou de
/// outro hóspede do período). Copiado do módulo Pets pelo contrato público: o Listings não
/// referencia o Pets (§5).
/// </summary>
/// <param name="Species">Valor de <c>pets.species</c> (<c>dog</c>, <c>cat</c>...).</param>
/// <param name="Size">Valor de <c>pets.size</c> (<c>small</c>, <c>medium</c>, <c>large</c>), quando tem.</param>
public sealed record CompatibilityPet(
    Guid Id,
    string Name,
    string Species,
    string? Size,
    bool GoodWithDogs,
    bool GoodWithCats,
    bool GoodWithKids);
