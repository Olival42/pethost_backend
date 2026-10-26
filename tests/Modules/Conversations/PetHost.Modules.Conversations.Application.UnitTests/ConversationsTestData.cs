using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Shared.Contracts.Storage;
using PetHost.TestKit;

namespace PetHost.Modules.Conversations.Application.UnitTests;

/// <summary>
/// Dados de leitura dos testes da Application: os perfis da Camila (tutora) e da Dona Cida
/// (anfitriã), com os ids fixos do <see cref="ConversationBuilder"/>, e as linhas que a leitura projeta.
/// </summary>
internal static class ConversationsTestData
{
    public static readonly PartyProfile AvailableOwner = new(ConversationBuilder.OwnerId, IsAvailable: true);
    public static readonly PartyProfile SuspendedOwner = new(ConversationBuilder.OwnerId, IsAvailable: false);
    public static readonly PartyProfile AvailableHost = new(ConversationBuilder.HostId, IsAvailable: true);
    public static readonly PartyProfile SuspendedHost = new(ConversationBuilder.HostId, IsAvailable: false);

    public static readonly PartyInfo Owner = new(ConversationBuilder.OwnerId, ConversationBuilder.OwnerUserId, "Camila Souza", "https://images.pethost.local/avatars/camila.webp");
    public static readonly PartyInfo Host = new(ConversationBuilder.HostId, ConversationBuilder.HostUserId, "Dona Cida", AvatarUrl: null);

    public static IReadOnlyDictionary<Guid, PartyInfo> Owners() => new Dictionary<Guid, PartyInfo> { [Owner.Id] = Owner };

    public static IReadOnlyDictionary<Guid, PartyInfo> Hosts() => new Dictionary<Guid, PartyInfo> { [Host.Id] = Host };

    public static IReadOnlyDictionary<Guid, PartyInfo> Nobody() => new Dictionary<Guid, PartyInfo>();

    /// <summary>Começo da URL assinada falsa das fotos privadas: <c>SignedPhotoPrefix + chave</c>.</summary>
    public const string SignedPhotoPrefix = "https://signed.pethost.local/";

    /// <summary>Quando a URL assinada falsa vence.</summary>
    public static readonly DateTimeOffset SignedPhotoExpiresAt = ConversationBuilder.DefaultNow.AddMinutes(5);

    /// <summary>Links das fotos com um bucket privado falso: assina qualquer chave, sem rede.</summary>
    public static MessagePhotoLinks PhotoLinks() => new(SigningStorage().Object);

    /// <summary>Bucket privado falso que só assina.</summary>
    public static Mock<IPrivateImageStorage> SigningStorage()
    {
        var storage = new Mock<IPrivateImageStorage>();
        storage
            .Setup(s => s.SignUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => new SignedImageUrl(SignedPhotoPrefix + key, SignedPhotoExpiresAt));

        return storage;
    }

    /// <summary>A linha que a leitura projeta para <paramref name="conversation"/>.</summary>
    public static ConversationRow Row(Conversation conversation, int unread = 0, MessageRow? lastMessage = null) =>
        new(conversation.Id, conversation.OwnerId, conversation.HostId, conversation.LastMessageAt, conversation.CreatedAt, lastMessage, unread);

    /// <summary>Uma mensagem de texto como a leitura projeta.</summary>
    public static MessageRow MessageRow(Conversation conversation, ConversationParty sentBy, string body, DateTimeOffset sentAt)
    {
        var message = ConversationBuilder.Text(conversation, sentBy, body, sentAt: sentAt);

        return new MessageRow(message.Id, message.ConversationId, message.SentBy, message.Body, message.ImageUrl, message.ImageKey, message.BookingId, message.CreatedAt);
    }
}
