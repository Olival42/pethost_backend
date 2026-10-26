using FluentAssertions;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Storage;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Messages.Responses;

public sealed class MessageResponseTests
{
    private const string PhotoKey = "conversations/0192aa.webp";
    private const string SignedUrl = "https://signed.pethost.local/conversations/0192aa.webp";

    private static readonly DateTimeOffset SignedUntil = ConversationBuilder.DefaultNow.AddMinutes(5);

    private readonly Conversation _conversation = new ConversationBuilder().Build();

    [Fact]
    public void From_Should_CopyEveryFieldWithTheSideAsText_When_GivenAMessage()
    {
        // Arrange
        var message = Message.Photo(_conversation.Id, ConversationParty.Host, PhotoKey, "Dormindo", ConversationBuilder.BookingId, ConversationBuilder.DefaultNow).Value!;

        // Act
        var response = MessageResponse.From(message, new SignedImageUrl(SignedUrl, SignedUntil));

        // Assert
        response.Should().Be(new MessageResponse(
            message.Id.Value,
            _conversation.Id.Value,
            "host",
            "Dormindo",
            SignedUrl,
            SignedUntil.ToUnixTimeSeconds(),
            ConversationBuilder.BookingId,
            ConversationBuilder.DefaultNow));
    }

    [Fact]
    public void From_Should_CopyEveryFieldWithTheSideAsText_When_GivenARow()
    {
        // Arrange
        var row = ConversationsTestData.MessageRow(_conversation, ConversationParty.Owner, "Oi!", ConversationBuilder.DefaultNow);

        // Act
        var response = MessageResponse.From(row);

        // Assert
        response.Should().Be(new MessageResponse(row.Id.Value, _conversation.Id.Value, "owner", "Oi!", null, null, null, ConversationBuilder.DefaultNow));
    }
}
