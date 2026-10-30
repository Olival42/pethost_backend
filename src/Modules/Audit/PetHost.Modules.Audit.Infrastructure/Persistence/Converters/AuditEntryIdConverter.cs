using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Audit.Domain.Entries;

namespace PetHost.Modules.Audit.Infrastructure.Persistence.Converters;

/// <summary><see cref="AuditEntryId"/> ↔ <c>uuid</c>.</summary>
internal sealed class AuditEntryIdConverter : ValueConverter<AuditEntryId, Guid>
{
    public AuditEntryIdConverter()
        : base(id => id.Value, value => new AuditEntryId(value))
    {
    }
}
