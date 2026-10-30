using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Owners.Domain.Owners;

namespace PetHost.Modules.Owners.Infrastructure.Persistence.Converters;

/// <summary><see cref="OwnerId"/> ↔ <c>uuid</c>.</summary>
internal sealed class OwnerIdConverter : ValueConverter<OwnerId, Guid>
{
    public OwnerIdConverter()
        : base(id => id.Value, value => new OwnerId(value))
    {
    }
}
