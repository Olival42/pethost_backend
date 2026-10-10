namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>Identidade de <see cref="PetPhoto"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct PetPhotoId(Guid Value)
{
    public static PetPhotoId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
