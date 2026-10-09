namespace PetHost.Modules.Owners.Domain.Owners;

/// <summary>
/// Identidade própria de <see cref="Owner"/>, diferente do id da conta. UUID v7 gerado
/// pelo domínio, como nas outras tabelas.
/// </summary>
public readonly record struct OwnerId(Guid Value)
{
    public static OwnerId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
