namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// Identidade própria de <see cref="Host"/>, diferente do id da conta. UUID v7 gerado
/// pelo domínio, como nas outras tabelas.
/// </summary>
public readonly record struct HostId(Guid Value)
{
    public static HostId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
