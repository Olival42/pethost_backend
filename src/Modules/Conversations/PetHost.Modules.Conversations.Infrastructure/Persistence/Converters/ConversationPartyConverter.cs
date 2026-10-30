using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Converters;

/// <summary><see cref="ConversationParty"/> ↔ texto (<c>owner</c>, <c>host</c>): enum como string (§11).</summary>
internal sealed class ConversationPartyConverter : ValueConverter<ConversationParty, string>
{
    public ConversationPartyConverter()
        : base(party => ConversationPartyValues.ToWire(party), value => ConversationPartyValues.FromWire(value))
    {
    }
}
