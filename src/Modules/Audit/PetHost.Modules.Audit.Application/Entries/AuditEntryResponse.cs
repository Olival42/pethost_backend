using PetHost.Modules.Audit.Domain.Entries;

namespace PetHost.Modules.Audit.Application.Entries;

/// <summary>Um registro da trilha, como sai para o admin.</summary>
public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string TargetType,
    Guid? TargetId,
    Guid? ActorId,
    string? ActorRole,
    string? Reason,
    IReadOnlyDictionary<string, string?> Details,
    string? IpAddress,
    string? TraceId)
{
    public static AuditEntryResponse From(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new AuditEntryResponse(
            entry.Id.Value,
            entry.OccurredAt,
            entry.Action,
            entry.TargetType,
            entry.TargetId,
            entry.ActorId,
            entry.ActorRole,
            entry.Reason,
            entry.Details,
            entry.IpAddress,
            entry.TraceId);
    }
}
