namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// "Combina com seu pet" (escopo, regra 11): cruza a ficha do pet com o cantinho — o que ele
/// aceita (tipo e porte) e quem mora na casa (cães, gatos, crianças) — e com os outros hóspedes
/// do período. <b>Só avisa, nunca bloqueia</b>: o pedido de reserva tem as regras dele.
/// </summary>
public static class PetCompatibility
{
    /// <summary>O cantinho não aceita esse tipo de pet.</summary>
    public const string SpeciesNotAccepted = "species_not_accepted";

    /// <summary>O cantinho não aceita o porte do pet (cachorro ou gato).</summary>
    public const string SizeNotAccepted = "size_not_accepted";

    /// <summary>O pet não se dá com cães, e na casa tem cão.</summary>
    public const string HomeHasDogs = "home_has_dogs";

    /// <summary>O pet não se dá com gatos, e na casa tem gato.</summary>
    public const string HomeHasCats = "home_has_cats";

    /// <summary>O pet não se dá com crianças, e na casa tem criança.</summary>
    public const string HomeHasKids = "home_has_kids";

    /// <summary>O pet não se dá com cães, e um hóspede do período é cão.</summary>
    public const string GuestIsDog = "guest_is_dog";

    /// <summary>O pet não se dá com gatos, e um hóspede do período é gato.</summary>
    public const string GuestIsCat = "guest_is_cat";

    /// <summary>O pet é cão, e um hóspede do período não se dá com cães.</summary>
    public const string GuestDislikesDogs = "guest_dislikes_dogs";

    /// <summary>O pet é gato, e um hóspede do período não se dá com gatos.</summary>
    public const string GuestDislikesCats = "guest_dislikes_cats";

    /// <summary>O pet é de porte pequeno, e um hóspede do período é cão grande.</summary>
    public const string GuestLargeDog = "guest_large_dog";

    private const string Dog = "dog";
    private const string Cat = "cat";
    private const string Small = "small";
    private const string Medium = "medium";
    private const string Large = "large";

    /// <summary>Todos os códigos, na ordem em que os avisos saem.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        SpeciesNotAccepted, SizeNotAccepted, HomeHasDogs, HomeHasCats, HomeHasKids,
        GuestIsDog, GuestIsCat, GuestDislikesDogs, GuestDislikesCats, GuestLargeDog,
    ];

    /// <summary>
    /// Os avisos para <paramref name="pet"/> no cantinho: primeiro os do cantinho e da casa, depois
    /// os de cada hóspede, na ordem recebida. Lista vazia = combina.
    /// </summary>
    /// <param name="guests">Os outros pets hospedados no período; vazio quando não há período.</param>
    public static IReadOnlyList<CompatibilityWarning> Check(
        Listing listing,
        CompatibilityPet pet,
        IReadOnlyList<CompatibilityPet> guests)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(pet);
        ArgumentNullException.ThrowIfNull(guests);

        var details = listing.Details;
        var warnings = new List<CompatibilityWarning>();

        if (!Accepts(details.AcceptedSpecies, pet.Species))
            warnings.Add(new CompatibilityWarning(SpeciesNotAccepted));
        else if (pet.Species is Dog or Cat && !AcceptsSize(details, pet.Size))
            warnings.Add(new CompatibilityWarning(SizeNotAccepted));

        if (!pet.GoodWithDogs && details.HasDogs)
            warnings.Add(new CompatibilityWarning(HomeHasDogs));

        if (!pet.GoodWithCats && details.HasCats)
            warnings.Add(new CompatibilityWarning(HomeHasCats));

        if (!pet.GoodWithKids && details.HasKids)
            warnings.Add(new CompatibilityWarning(HomeHasKids));

        foreach (var guest in guests)
            warnings.AddRange(GuestWarnings(pet, guest));

        return warnings;
    }

    private static IEnumerable<CompatibilityWarning> GuestWarnings(CompatibilityPet pet, CompatibilityPet guest)
    {
        if (!pet.GoodWithDogs && guest.Species == Dog)
            yield return new CompatibilityWarning(GuestIsDog, guest);

        if (!pet.GoodWithCats && guest.Species == Cat)
            yield return new CompatibilityWarning(GuestIsCat, guest);

        if (pet.Species == Dog && !guest.GoodWithDogs)
            yield return new CompatibilityWarning(GuestDislikesDogs, guest);

        if (pet.Species == Cat && !guest.GoodWithCats)
            yield return new CompatibilityWarning(GuestDislikesCats, guest);

        if (pet.Size == Small && guest.Species == Dog && guest.Size == Large)
            yield return new CompatibilityWarning(GuestLargeDog, guest);
    }

    /// <summary>Espécie fora da lista conhecida conta como não aceita.</summary>
    private static bool Accepts(AcceptedSpecies accepted, string species) =>
        AcceptedSpeciesValues.TryParse([species], out var parsed) && accepted.HasFlag(parsed);

    /// <summary>Cachorro ou gato sem porte informado não gera aviso de porte.</summary>
    private static bool AcceptsSize(ListingDetails details, string? size) => size switch
    {
        Small => details.AcceptsSmall,
        Medium => details.AcceptsMedium,
        Large => details.AcceptsLarge,
        _ => true,
    };
}
