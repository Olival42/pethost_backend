using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Api.Seed;

/// <summary>Pet de teste, nos valores da API (<c>dog</c>, <c>small</c>, <c>yyyy-MM-dd</c>...).</summary>
public sealed record TestPet(
    string Name,
    string Species,
    string? Breed,
    string? Size,
    string BirthDate,
    string Sex,
    bool IsNeutered,
    bool IsVaccinated,
    bool GoodWithDogs,
    bool GoodWithCats,
    bool GoodWithKids,
    decimal? WeightKg = null,
    string? FeedingNotes = null,
    string? MedicationNotes = null,
    string? Notes = null);

/// <summary>Cantinho de teste, com a coordenada do endereço (a subida não consulta o mapa).</summary>
public sealed record TestListing(
    string Title,
    string Description,
    string HomeType,
    bool HasYard,
    bool HasDogs,
    bool HasCats,
    bool HasKids,
    IReadOnlyList<string> AcceptedSpecies,
    bool AcceptsSmall,
    bool AcceptsMedium,
    bool AcceptsLarge,
    int Capacity,
    decimal NightlyRate,
    string? HouseRules,
    IReadOnlyList<string> AvailableWeekdays,
    decimal Latitude,
    decimal Longitude);

/// <summary>Pessoa de teste: a conta e o CPF.</summary>
public sealed record TestPerson(
    string FullName,
    string Email,
    string Phone,
    string BirthDate,
    string Cpf,
    AddressData Address);

public sealed record TestOwner(TestPerson Person, IReadOnlyList<TestPet> Pets);

/// <param name="Pets">Pets que moram na casa do anfitrião.</param>
public sealed record TestHost(TestPerson Person, TestListing Listing, IReadOnlyList<TestPet> Pets);

/// <summary>
/// Os dados de teste: todos em Maringá (PR), perto o bastante para aparecerem juntos na busca por
/// raio. Tudo fictício.
/// </summary>
public static class TestDataCatalog
{
    private static readonly string[] EveryDay = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    public static IReadOnlyList<TestOwner> Owners { get; } =
    [
        new(
            new("Ana Souza (teste)", "tutora.ana@pethost.local", "44991110001", "1992-05-14", "529.982.247-25",
                new("87020025", "Avenida Brasil", "1500", "Apto 32", "Zona 1", "Maringá", "PR")),
            [
                new("Thor", "dog", "Labrador", "large", "2019-08-02", "male", IsNeutered: true, IsVaccinated: true,
                    GoodWithDogs: true, GoodWithCats: false, GoodWithKids: true, WeightKg: 32.5m,
                    FeedingNotes: "Ração 2x ao dia (manhã e noite), 300 g cada."),
                new("Mel", "cat", "SRD", "small", "2021-02-10", "female", IsNeutered: true, IsVaccinated: true,
                    GoodWithDogs: false, GoodWithCats: true, GoodWithKids: true, WeightKg: 4.2m,
                    Notes: "Tímida nos primeiros dias."),
            ]),
        new(
            new("Bruno Lima (teste)", "tutor.bruno@pethost.local", "44991110002", "1988-11-30", "111.444.777-35",
                new("87013230", "Rua Néo Alves Martins", "2100", null, "Zona 3", "Maringá", "PR")),
            [
                new("Pipoca", "dog", "Shih-tzu", "small", "2020-04-21", "female", IsNeutered: false, IsVaccinated: true,
                    GoodWithDogs: true, GoodWithCats: true, GoodWithKids: true, WeightKg: 6m,
                    MedicationNotes: "Colírio 1 gota em cada olho, 1x ao dia."),
                new("Kiwi", "cockatiel", null, null, "2022-09-05", "male", IsNeutered: false, IsVaccinated: false,
                    GoodWithDogs: false, GoodWithCats: false, GoodWithKids: true,
                    FeedingNotes: "Mistura de sementes e frutas picadas."),
            ]),
    ];

    public static IReadOnlyList<TestHost> Hosts { get; } =
    [
        new(
            new("Carla Mendes (teste)", "anfitria.carla@pethost.local", "44992220001", "1979-07-08", "390.533.447-05",
                new("87020230", "Rua Pioneiro Antônio de Ganello", "450", null, "Zona 7", "Maringá", "PR")),
            new("Casa com quintal na Zona 7",
                "Casa térrea com quintal gramado e cercado. Passeios duas vezes por dia e muito carinho.",
                "house", HasYard: true, HasDogs: true, HasCats: false, HasKids: false,
                ["dog"], AcceptsSmall: true, AcceptsMedium: true, AcceptsLarge: true,
                Capacity: 3, NightlyRate: 80m, "Sem acesso aos quartos. Chegada até as 20h.",
                EveryDay, Latitude: -23.410000m, Longitude: -51.933000m),
            [
                new("Bob", "dog", "Vira-lata", "medium", "2018-01-15", "male", IsNeutered: true, IsVaccinated: true,
                    GoodWithDogs: true, GoodWithCats: false, GoodWithKids: true, WeightKg: 15m),
            ]),
        new(
            new("Diego Rocha (teste)", "anfitriao.diego@pethost.local", "44992220002", "1995-03-22", "935.411.347-80",
                new("87013000", "Avenida Herval", "800", "Apto 101", "Zona 1", "Maringá", "PR")),
            new("Apartamento tranquilo para gatos",
                "Apartamento telado, com arranhadores e caixas de areia limpas todo dia. Ideal para gatos.",
                "apartment", HasYard: false, HasDogs: false, HasCats: true, HasKids: false,
                ["cat"], AcceptsSmall: true, AcceptsMedium: true, AcceptsLarge: false,
                Capacity: 2, NightlyRate: 55m, null,
                ["monday", "tuesday", "wednesday", "thursday", "friday"], Latitude: -23.420500m, Longitude: -51.937800m),
            [
                new("Luna", "cat", "Siamês", "small", "2020-12-01", "female", IsNeutered: true, IsVaccinated: true,
                    GoodWithDogs: false, GoodWithCats: true, GoodWithKids: false, WeightKg: 3.8m),
            ]),
        new(
            new("Elisa Prado (teste)", "anfitria.elisa@pethost.local", "44992220003", "1985-10-03", "286.754.031-35",
                new("87033000", "Rua das Palmeiras", "250", null, "Jardim Alvorada", "Maringá", "PR")),
            new("Chácara urbana para cães, gatos e aves",
                "Casa ampla com quintal, viveiro para aves e espaço para coelhos. Família com crianças.",
                "house", HasYard: true, HasDogs: false, HasCats: false, HasKids: true,
                ["dog", "cat", "cockatiel", "parakeet", "rabbit"], AcceptsSmall: true, AcceptsMedium: true, AcceptsLarge: false,
                Capacity: 5, NightlyRate: 65.5m, "Pets precisam estar vacinados.",
                EveryDay, Latitude: -23.395600m, Longitude: -51.951200m),
            []),
    ];
}
