using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Converters;

/// <summary><see cref="PetId"/> ↔ <c>uuid</c>.</summary>
internal sealed class PetIdConverter : ValueConverter<PetId, Guid>
{
    public PetIdConverter()
        : base(id => id.Value, value => new PetId(value))
    {
    }
}
