namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>Identidade de <see cref="Pet"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct PetId(Guid Value)
{
    public static PetId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
