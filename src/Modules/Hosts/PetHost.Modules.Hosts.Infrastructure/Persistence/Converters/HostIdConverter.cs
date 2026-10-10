using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Converters;

/// <summary><see cref="HostId"/> ↔ <c>uuid</c>.</summary>
internal sealed class HostIdConverter : ValueConverter<HostId, Guid>
{
    public HostIdConverter()
        : base(id => id.Value, value => new HostId(value))
    {
    }
}
