namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>Sexo do animal.</summary>
public enum PetSex
{
    Male,
    Female,

    /// <summary>
    /// Não se sabe: em aves, peixes e vários exóticos o sexo só se descobre com exame.
    /// Melhor admitir do que forçar um chute.
    /// </summary>
    Unknown,
}
