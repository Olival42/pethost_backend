namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Identidade de <see cref="HostApplication"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct HostApplicationId(Guid Value)
{
    public static HostApplicationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
