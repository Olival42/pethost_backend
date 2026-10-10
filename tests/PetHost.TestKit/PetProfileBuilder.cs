using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.TestKit;

/// <summary>
/// Monta a ficha de um pet para teste: por padrão, a Pipoca — cachorra média, válida em
/// tudo. Cada <c>With...</c> troca um campo; o teste declara só o que importa.
/// </summary>
public sealed class PetProfileBuilder
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private PetProfileData _data = new(
        Species: "dog",
        SpeciesDescription: null,
        Name: "Pipoca",
        PhotoUrl: "https://cdn.pethost.com/pipoca.png",
        Breed: "SRD",
        Size: "medium",
        BirthDate: "2022-03-15",
        Sex: "female",
        IsNeutered: true,
        IsVaccinated: true,
        MedicationNotes: null,
        FeedingNotes: "Ração 2x ao dia",
        GoodWithDogs: true,
        GoodWithCats: false,
        GoodWithKids: true,
        VetContact: "Dra. Ana — (44) 3222-0000",
        Notes: "Morre de medo de fogos.",
        WeightKg: 14.5m,
        Microchip: "985112004567890",
        Allergies: "Frango");

    public PetProfileBuilder WithName(string? name)
    {
        _data = _data with { Name = name };
        return this;
    }

    /// <summary>Troca a espécie, ajustando porte e descrição para continuar válida.</summary>
    public PetProfileBuilder AsSpecies(string species, string? description = null) =>
        With(_data with
        {
            Species = species,
            Size = species == "dog" ? _data.Size ?? "medium" : null,
            SpeciesDescription = species == "exotic" ? description ?? "Iguana verde" : null,
        });

    public PetProfileBuilder With(PetProfileData data)
    {
        _data = data;
        return this;
    }

    public PetProfileData BuildData() => _data;

    /// <summary>A ficha validada. Estoura se o builder montou algo inválido (bug do teste).</summary>
    public PetProfile Build()
    {
        var profile = PetProfile.Create(_data, DefaultNow);

        return profile.IsSuccess
            ? profile.Value!
            : throw new InvalidOperationException($"Invalid test pet: {profile.FirstError?.Message}");
    }

    /// <summary>Um pet cadastrado com esta ficha.</summary>
    public Pet BuildPet(PetKeeper keeper) => Pet.Create(keeper, Build(), DefaultNow);
}
