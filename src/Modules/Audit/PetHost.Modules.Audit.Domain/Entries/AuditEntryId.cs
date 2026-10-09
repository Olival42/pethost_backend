namespace PetHost.Modules.Audit.Domain.Entries;

/// <summary>Identidade de <see cref="AuditEntry"/>. UUID v7: ordenado pelo tempo, como os fatos.</summary>
public readonly record struct AuditEntryId(Guid Value)
{
    public static AuditEntryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
