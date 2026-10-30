namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>
/// Tipo do animal. Lista fechada com os pets mais comuns; o que não está aqui entra como
/// <see cref="Exotic"/>, com a descrição em texto (<c>species_description</c>).
/// </summary>
public enum PetSpecies
{
    Dog,
    Cat,

    /// <summary>Calopsita.</summary>
    Cockatiel,

    /// <summary>Papagaio.</summary>
    Parrot,

    /// <summary>Periquito.</summary>
    Parakeet,

    /// <summary>Canário.</summary>
    Canary,

    /// <summary>Coelho.</summary>
    Rabbit,

    Hamster,

    /// <summary>Porquinho-da-índia.</summary>
    GuineaPig,

    /// <summary>Peixe.</summary>
    Fish,

    /// <summary>Tartaruga ou jabuti.</summary>
    Turtle,

    /// <summary>Qualquer outro: o tutor descreve o animal; o anfitrião decide se aceita.</summary>
    Exotic,
}
