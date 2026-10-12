namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// Tipos de pet que o cantinho aceita, como conjunto de bits (coluna <c>accepted_species</c>).
/// Mesma lista de <c>pets.species</c> no módulo Pets — repetida aqui de propósito: um módulo
/// não referencia o outro (§5). Aceitar <see cref="Exotic"/> quer dizer "aceito conversar":
/// o anfitrião lê a descrição do animal e decide no pedido.
/// </summary>
[Flags]
public enum AcceptedSpecies
{
    None = 0,
    Dog = 1 << 0,
    Cat = 1 << 1,

    /// <summary>Calopsita.</summary>
    Cockatiel = 1 << 2,

    /// <summary>Papagaio.</summary>
    Parrot = 1 << 3,

    /// <summary>Periquito.</summary>
    Parakeet = 1 << 4,

    /// <summary>Canário.</summary>
    Canary = 1 << 5,

    /// <summary>Coelho.</summary>
    Rabbit = 1 << 6,

    Hamster = 1 << 7,

    /// <summary>Porquinho-da-índia.</summary>
    GuineaPig = 1 << 8,

    /// <summary>Peixe.</summary>
    Fish = 1 << 9,

    /// <summary>Tartaruga ou jabuti.</summary>
    Turtle = 1 << 10,

    /// <summary>Qualquer outro animal: o anfitrião decide no pedido.</summary>
    Exotic = 1 << 11,
}
