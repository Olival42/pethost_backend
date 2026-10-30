using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Conversations.Domain.Reports;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Converters;

/// <summary><see cref="MessageReportId"/> ↔ <c>uuid</c>.</summary>
internal sealed class MessageReportIdConverter : ValueConverter<MessageReportId, Guid>
{
    public MessageReportIdConverter()
        : base(id => id.Value, value => new MessageReportId(value))
    {
    }
}
