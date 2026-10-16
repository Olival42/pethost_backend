using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Conversations.Domain.Messages;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Converters;

/// <summary><see cref="MessageId"/> ↔ <c>uuid</c>.</summary>
internal sealed class MessageIdConverter : ValueConverter<MessageId, Guid>
{
    public MessageIdConverter()
        : base(id => id.Value, value => new MessageId(value))
    {
    }
}
