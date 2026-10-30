using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Listings.CheckPetCompatibility;

/// <summary>"Combina com seu pet": os avisos do pet no cantinho. Só avisa, não bloqueia o pedido.</summary>
/// <param name="Matches">Nenhum aviso: "A Pipoca combina com essa casa".</param>
/// <param name="Warnings">Avisos na ordem: cantinho e casa primeiro, depois cada hóspede do período.</param>
/// <param name="Guests">
/// Os outros pets hospedados no período (de outros tutores), na ordem de chegada. Nulo (omitido)
/// quando não vêm as datas.
/// </param>
public sealed record PetCompatibilityResponse(
    Guid ListingId,
    Guid PetId,
    string PetName,
    bool Matches,
    IReadOnlyList<CompatibilityWarningResponse> Warnings,
    IReadOnlyList<CompatibilityGuestResponse>? Guests)
{
    public static PetCompatibilityResponse From(
        Listing listing,
        CompatibilityPet pet,
        IReadOnlyList<CompatibilityWarning> warnings,
        IReadOnlyList<CompatibilityPet>? guests)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(pet);
        ArgumentNullException.ThrowIfNull(warnings);

        return new PetCompatibilityResponse(
            listing.Id.Value,
            pet.Id,
            pet.Name,
            warnings.Count == 0,
            [.. warnings.Select(w => new CompatibilityWarningResponse(w.Code, w.Guest is null ? null : CompatibilityGuestResponse.From(w.Guest)))],
            guests is null ? null : [.. guests.Select(CompatibilityGuestResponse.From)]);
    }
}

/// <summary>Um aviso. O app monta o texto a partir do código e dos nomes.</summary>
/// <param name="Code">
/// <c>species_not_accepted</c>, <c>size_not_accepted</c>, <c>home_has_dogs</c>, <c>home_has_cats</c>,
/// <c>home_has_kids</c>, <c>guest_is_dog</c>, <c>guest_is_cat</c>, <c>guest_dislikes_dogs</c>,
/// <c>guest_dislikes_cats</c> ou <c>guest_large_dog</c>.
/// </param>
/// <param name="Guest">O hóspede que gerou o aviso; omitido nos avisos sobre o cantinho e a casa.</param>
public sealed record CompatibilityWarningResponse(string Code, CompatibilityGuestResponse? Guest);

/// <summary>Outro pet hospedado no período: nome, tipo e porte. Nada do tutor dele.</summary>
public sealed record CompatibilityGuestResponse(string Name, string Species, string? Size)
{
    public static CompatibilityGuestResponse From(CompatibilityPet pet)
    {
        ArgumentNullException.ThrowIfNull(pet);

        return new CompatibilityGuestResponse(pet.Name, pet.Species, pet.Size);
    }
}
