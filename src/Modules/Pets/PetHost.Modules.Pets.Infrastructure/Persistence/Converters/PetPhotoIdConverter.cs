using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Converters;

/// <summary><see cref="PetPhotoId"/> ↔ <c>uuid</c>.</summary>
internal sealed class PetPhotoIdConverter : ValueConverter<PetPhotoId, Guid>
{
    public PetPhotoIdConverter()
        : base(id => id.Value, value => new PetPhotoId(value))
    {
    }
}
