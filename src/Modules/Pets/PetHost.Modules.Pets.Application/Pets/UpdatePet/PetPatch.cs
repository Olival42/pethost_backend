using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Application.Pets.UpdatePet;

/// <summary>
/// Mescla o PATCH com a ficha atual. O resultado ainda não está validado: vai para
/// <see cref="PetProfile.Create"/>, que confere a ficha inteira.
/// </summary>
public static class PetPatch
{
    /// <summary>
    /// Campo ausente (<c>null</c>) mantém o atual; texto vazio passa adiante e limpa o campo.
    /// Quando a espécie muda, porte e descrição que não vieram no PATCH são limpos se a nova
    /// espécie não os tem (porte só em cachorro e gato; descrição só em exótico): o cliente
    /// não precisa mandar <c>""</c> para isso. Cachorro ↔ gato mantém o porte.
    /// </summary>
    public static PetProfileData Apply(PetProfileData current, UpdatePetCommand patch)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(patch);

        var speciesChanged = patch.Species is not null
            && !string.Equals(patch.Species.Trim(), current.Species, StringComparison.OrdinalIgnoreCase);

        // Cachorro → gato mantém o porte; para uma espécie sem porte, ele cai.
        var keepsSize = !speciesChanged
            || (PetSpeciesValues.TryParse(patch.Species, out var newSpecies) && PetProfile.HasSize(newSpecies));

        return new PetProfileData(
            patch.Species ?? current.Species,
            patch.SpeciesDescription ?? (speciesChanged ? null : current.SpeciesDescription),
            patch.Name ?? current.Name,
            patch.PhotoUrl ?? current.PhotoUrl,
            patch.Breed ?? current.Breed,
            patch.Size ?? (keepsSize ? current.Size : null),
            patch.BirthDate ?? current.BirthDate,
            patch.Sex ?? current.Sex,
            patch.IsNeutered ?? current.IsNeutered,
            patch.IsVaccinated ?? current.IsVaccinated,
            patch.MedicationNotes ?? current.MedicationNotes,
            patch.FeedingNotes ?? current.FeedingNotes,
            patch.GoodWithDogs ?? current.GoodWithDogs,
            patch.GoodWithCats ?? current.GoodWithCats,
            patch.GoodWithKids ?? current.GoodWithKids,
            patch.VetContact ?? current.VetContact,
            patch.Notes ?? current.Notes,
            patch.WeightKg ?? current.WeightKg,
            patch.Microchip ?? current.Microchip,
            patch.Allergies ?? current.Allergies);
    }

    /// <summary>
    /// Nomes (camelCase, como no JSON) dos campos que mudaram entre as duas fichas. Vai
    /// para a trilha de auditoria — pelo nome, nunca pelo valor.
    /// </summary>
    public static IReadOnlyList<string> ChangedFields(PetProfileData before, PetProfileData after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var fields = new (string Name, object? Before, object? After)[]
        {
            ("species", before.Species, after.Species),
            ("speciesDescription", before.SpeciesDescription, after.SpeciesDescription),
            ("name", before.Name, after.Name),
            ("photoUrl", before.PhotoUrl, after.PhotoUrl),
            ("breed", before.Breed, after.Breed),
            ("size", before.Size, after.Size),
            ("birthDate", before.BirthDate, after.BirthDate),
            ("sex", before.Sex, after.Sex),
            ("isNeutered", before.IsNeutered, after.IsNeutered),
            ("isVaccinated", before.IsVaccinated, after.IsVaccinated),
            ("medicationNotes", before.MedicationNotes, after.MedicationNotes),
            ("feedingNotes", before.FeedingNotes, after.FeedingNotes),
            ("goodWithDogs", before.GoodWithDogs, after.GoodWithDogs),
            ("goodWithCats", before.GoodWithCats, after.GoodWithCats),
            ("goodWithKids", before.GoodWithKids, after.GoodWithKids),
            ("vetContact", before.VetContact, after.VetContact),
            ("notes", before.Notes, after.Notes),
            ("weightKg", before.WeightKg, after.WeightKg),
            ("microchip", before.Microchip, after.Microchip),
            ("allergies", before.Allergies, after.Allergies),
        };

        return [.. fields.Where(f => !Equals(f.Before, f.After)).Select(f => f.Name)];
    }
}
