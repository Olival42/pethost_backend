using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Converters;

/// <summary><see cref="ReviewId"/> ↔ <c>uuid</c>.</summary>
internal sealed class ReviewIdConverter : ValueConverter<ReviewId, Guid>
{
    public ReviewIdConverter()
        : base(id => id.Value, value => new ReviewId(value))
    {
    }
}
