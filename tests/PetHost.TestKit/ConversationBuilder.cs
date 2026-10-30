using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;

namespace PetHost.TestKit;

/// <summary>
/// Monta uma conversa para teste: por padrão, a Camila (tutora) puxou conversa com a Dona Cida
/// (anfitriã) no domingo, 11/10/2026. Cada <c>With...</c> troca uma parte; o teste declara só o
/// que importa. Os ids de perfil e de conta são fixos, para as asserções compararem.
/// </summary>
public sealed class ConversationBuilder
{
    /// <summary>Domingo, 11/10/2026, 09:00 em Brasília.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Perfil de tutora da Camila (<c>owner.owners.id</c>).</summary>
    public static readonly Guid OwnerId = Guid.Parse("0199c000-0000-7000-8000-000000000001");

    /// <summary>Perfil de anfitriã da Dona Cida (<c>host.hosts.id</c>).</summary>
    public static readonly Guid HostId = Guid.Parse("0199c000-0000-7000-8000-000000000002");

    /// <summary>Conta da Camila (claim <c>sub</c>).</summary>
    public static readonly Guid OwnerUserId = Guid.Parse("0199c000-0000-7000-8000-000000000011");

    /// <summary>Conta da Dona Cida (claim <c>sub</c>).</summary>
    public static readonly Guid HostUserId = Guid.Parse("0199c000-0000-7000-8000-000000000012");

    /// <summary>O cantinho da Dona Cida, onde a Camila tocou em "Puxar conversa".</summary>
    public static readonly Guid ListingId = Guid.Parse("0199c000-0000-7000-8000-000000000021");

    /// <summary>Uma estadia em andamento da dupla.</summary>
    public static readonly Guid BookingId = Guid.Parse("0199c000-0000-7000-8000-000000000031");

    private Guid _ownerId = OwnerId;
    private Guid _hostId = HostId;
    private DateTimeOffset _startedAt = DefaultNow;

    public ConversationBuilder WithOwner(Guid ownerId)
    {
        _ownerId = ownerId;
        return this;
    }

    public ConversationBuilder WithHost(Guid hostId)
    {
        _hostId = hostId;
        return this;
    }

    public ConversationBuilder StartedAt(DateTimeOffset startedAt)
    {
        _startedAt = startedAt;
        return this;
    }

    public Conversation Build() => Conversation.Start(_ownerId, _hostId, _startedAt);

    /// <summary>Uma mensagem de texto válida na conversa, enviada em <paramref name="sentAt"/> (padrão: <see cref="DefaultNow"/>).</summary>
    public static Message Text(
        Conversation conversation,
        ConversationParty sentBy,
        string body = "Oi! A Pipoca tem medo de fogos, tudo bem?",
        Guid? bookingId = null,
        DateTimeOffset? sentAt = null)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return Message.Text(conversation.Id, sentBy, body, bookingId, sentAt ?? DefaultNow).Value!;
    }
}
