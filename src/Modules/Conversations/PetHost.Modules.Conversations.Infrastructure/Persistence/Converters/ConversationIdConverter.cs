using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Converters;

/// <summary><see cref="ConversationId"/> ↔ <c>uuid</c>.</summary>
internal sealed class ConversationIdConverter : ValueConverter<ConversationId, Guid>
{
    public ConversationIdConverter()
        : base(id => id.Value, value => new ConversationId(value))
    {
    }
}
