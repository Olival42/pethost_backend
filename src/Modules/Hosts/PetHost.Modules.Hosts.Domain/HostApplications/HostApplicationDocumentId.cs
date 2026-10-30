namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Identidade de <see cref="HostApplicationDocument"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct HostApplicationDocumentId(Guid Value)
{
    public static HostApplicationDocumentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
