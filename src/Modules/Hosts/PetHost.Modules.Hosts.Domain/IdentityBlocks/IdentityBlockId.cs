namespace PetHost.Modules.Hosts.Domain.IdentityBlocks;

/// <summary>Identidade de <see cref="IdentityBlock"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct IdentityBlockId(Guid Value)
{
    public static IdentityBlockId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
