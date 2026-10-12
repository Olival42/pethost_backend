using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Listings.UpdateListing;

/// <summary>
/// Mescla o PATCH com o cantinho atual. O resultado ainda não está validado: vai para
/// <see cref="ListingDetails.Create"/> e <see cref="ListingAddress.Create"/>, que conferem tudo.
/// </summary>
public static class ListingPatch
{
    /// <summary>Campo ausente (<c>null</c>) mantém o atual; texto vazio passa adiante e limpa o campo; lista substitui.</summary>
    public static ListingDetailsData ApplyDetails(ListingDetailsData current, UpdateListingCommand patch)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(patch);

        return new ListingDetailsData(
            patch.Title ?? current.Title,
            patch.Description ?? current.Description,
            patch.HomeType ?? current.HomeType,
            patch.HasYard ?? current.HasYard,
            patch.HasDogs ?? current.HasDogs,
            patch.HasCats ?? current.HasCats,
            patch.HasKids ?? current.HasKids,
            patch.AcceptedSpecies ?? current.AcceptedSpecies,
            patch.AcceptsSmall ?? current.AcceptsSmall,
            patch.AcceptsMedium ?? current.AcceptsMedium,
            patch.AcceptsLarge ?? current.AcceptsLarge,
            patch.Capacity ?? current.Capacity,
            patch.NightlyRate ?? current.NightlyRate,
            patch.HouseRules ?? current.HouseRules,
            patch.AvailableWeekdays ?? current.AvailableWeekdays);
    }

    /// <summary>Endereço parcial: cada campo ausente mantém o atual; complemento <c>""</c> limpa.</summary>
    public static ListingAddressData ApplyAddress(ListingAddressData current, ListingAddressData? patch)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (patch is null)
            return current;

        return new ListingAddressData(
            patch.ZipCode ?? current.ZipCode,
            patch.Street ?? current.Street,
            patch.Number ?? current.Number,
            patch.Complement ?? current.Complement,
            patch.Neighborhood ?? current.Neighborhood,
            patch.City ?? current.City,
            patch.State ?? current.State);
    }

    /// <summary>
    /// Nomes (camelCase, como no JSON) dos campos que mudaram. Vai para a trilha de auditoria —
    /// pelo nome, nunca pelo valor.
    /// </summary>
    public static IReadOnlyList<string> ChangedFields(
        ListingDetailsData beforeDetails,
        ListingDetailsData afterDetails,
        ListingAddressData beforeAddress,
        ListingAddressData afterAddress)
    {
        ArgumentNullException.ThrowIfNull(beforeDetails);
        ArgumentNullException.ThrowIfNull(afterDetails);
        ArgumentNullException.ThrowIfNull(beforeAddress);
        ArgumentNullException.ThrowIfNull(afterAddress);

        var fields = new (string Name, object? Before, object? After)[]
        {
            ("title", beforeDetails.Title, afterDetails.Title),
            ("description", beforeDetails.Description, afterDetails.Description),
            ("homeType", beforeDetails.HomeType, afterDetails.HomeType),
            ("hasYard", beforeDetails.HasYard, afterDetails.HasYard),
            ("hasDogs", beforeDetails.HasDogs, afterDetails.HasDogs),
            ("hasCats", beforeDetails.HasCats, afterDetails.HasCats),
            ("hasKids", beforeDetails.HasKids, afterDetails.HasKids),
            ("acceptedSpecies", Joined(beforeDetails.AcceptedSpecies), Joined(afterDetails.AcceptedSpecies)),
            ("acceptsSmall", beforeDetails.AcceptsSmall, afterDetails.AcceptsSmall),
            ("acceptsMedium", beforeDetails.AcceptsMedium, afterDetails.AcceptsMedium),
            ("acceptsLarge", beforeDetails.AcceptsLarge, afterDetails.AcceptsLarge),
            ("capacity", beforeDetails.Capacity, afterDetails.Capacity),
            ("nightlyRate", beforeDetails.NightlyRate, afterDetails.NightlyRate),
            ("houseRules", beforeDetails.HouseRules, afterDetails.HouseRules),
            ("availableWeekdays", Joined(beforeDetails.AvailableWeekdays), Joined(afterDetails.AvailableWeekdays)),
            ("address.zipCode", beforeAddress.ZipCode, afterAddress.ZipCode),
            ("address.street", beforeAddress.Street, afterAddress.Street),
            ("address.number", beforeAddress.Number, afterAddress.Number),
            ("address.complement", beforeAddress.Complement, afterAddress.Complement),
            ("address.neighborhood", beforeAddress.Neighborhood, afterAddress.Neighborhood),
            ("address.city", beforeAddress.City, afterAddress.City),
            ("address.state", beforeAddress.State, afterAddress.State),
        };

        return [.. fields.Where(f => !Equals(f.Before, f.After)).Select(f => f.Name)];
    }

    /// <summary>Lista como texto, para comparar por valor (as listas vêm normalizadas pelo domínio).</summary>
    private static string? Joined(IReadOnlyList<string?>? values) => values is null ? null : string.Join(",", values);
}
