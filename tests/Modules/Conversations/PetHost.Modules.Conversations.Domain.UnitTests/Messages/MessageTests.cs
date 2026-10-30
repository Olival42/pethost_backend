using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Messages;

public sealed class MessageTests
{
    private const string PhotoKey = "conversations/0199c000.webp";

    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow;
    private static readonly ConversationId ConversationId = new ConversationBuilder().Build().Id;

    [Fact]
    public void Text_Should_CreateTheMessageWithTheTrimmedBody_When_BodyIsValid()
    {
        // Act
        var result = Message.Text(ConversationId, ConversationParty.Owner, "  Oi, Dona Cida!  ", bookingId: null, Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var message = result.Value!;
        message.Id.Value.Should().NotBeEmpty();
        message.ConversationId.Should().Be(ConversationId);
        message.SentBy.Should().Be(ConversationParty.Owner);
        message.Body.Should().Be("Oi, Dona Cida!");
        message.ImageUrl.Should().BeNull();
        message.HasPhoto.Should().BeFalse();
        message.BookingId.Should().BeNull();
        message.ReadAt.Should().BeNull();
        message.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Text_Should_KeepTheBooking_When_SentDuringAStay()
    {
        // Act
        var result = Message.Text(ConversationId, ConversationParty.Host, "A Pipoca já comeu!", ConversationBuilder.BookingId, Now);

        // Assert
        result.Value!.BookingId.Should().Be(ConversationBuilder.BookingId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Text_Should_ReturnBodyRequired_When_BodyIsBlank(string? body)
    {
        // Act
        var result = Message.Text(ConversationId, ConversationParty.Owner, body, bookingId: null, Now);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be(ConversationsErrors.BodyRequired);
    }

    [Fact]
    public void Text_Should_ReturnBodyTooLong_When_BodyIsLongerThanTheLimit()
    {
        // Arrange
        var body = new string('a', Message.BodyMaxLength + 1);

        // Act
        var result = Message.Text(ConversationId, ConversationParty.Owner, body, bookingId: null, Now);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.BodyTooLong);
    }

    [Fact]
    public void Text_Should_AcceptTheBody_When_ItHasExactlyTheLimitAfterTrimming()
    {
        // Arrange
        var body = $"  {new string('a', Message.BodyMaxLength)}  ";

        // Act
        var result = Message.Text(ConversationId, ConversationParty.Owner, body, bookingId: null, Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Body.Should().HaveLength(Message.BodyMaxLength);
    }

    [Theory]
    [InlineData("antes\0depois")]
    [InlineData("sino\u0007")]
    [InlineData("apagar\u007f")]
    [InlineData("controle\u0085C1")]
    public void Text_Should_ReturnBodyInvalidCharacters_When_BodyHasAControlCharacter(string body)
    {
        // Act
        var result = Message.Text(ConversationId, ConversationParty.Owner, body, bookingId: null, Now);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be(ConversationsErrors.BodyInvalidCharacters);
    }

    [Fact]
    public void Text_Should_KeepLineBreaksAndTabs_When_BodyHasThem()
    {
        // Arrange
        const string body = "Linha 1\r\nLinha 2\n\tcom tab";

        // Act
        var result = Message.Text(ConversationId, ConversationParty.Owner, body, bookingId: null, Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Body.Should().Be(body);
    }

    [Fact]
    public void HasControlCharacters_Should_Throw_When_TextIsNull()
    {
        // Act
        var act = () => Message.HasControlCharacters(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Photo_Should_ReturnBodyInvalidCharacters_When_CaptionHasAControlCharacter()
    {
        // Act
        var result = Message.Photo(ConversationId, ConversationParty.Host, PhotoKey, "legenda\0", bookingId: null, Now);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be(ConversationsErrors.BodyInvalidCharacters);
    }

    [Fact]
    public void Photo_Should_CreateTheMessageWithUrlAndCaption_When_BothAreValid()
    {
        // Act
        var result = Message.Photo(ConversationId, ConversationParty.Host, PhotoKey, " Brincando no quintal ", ConversationBuilder.BookingId, Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var message = result.Value!;
        message.ImageKey.Should().Be(PhotoKey);
        message.ImageUrl.Should().BeNull("fotos novas não têm URL pública");
        message.HasPhoto.Should().BeTrue();
        message.Body.Should().Be("Brincando no quintal");
        message.SentBy.Should().Be(ConversationParty.Host);
        message.BookingId.Should().Be(ConversationBuilder.BookingId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Photo_Should_LeaveTheBodyEmpty_When_CaptionIsBlank(string? caption)
    {
        // Act
        var result = Message.Photo(ConversationId, ConversationParty.Host, PhotoKey, caption, bookingId: null, Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Body.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://images.pethost.local/conversations/foto.webp")]
    [InlineData("avatars/foto.webp")]
    [InlineData("conversations/../host-applications/doc.webp")]
    public void Photo_Should_ReturnImageKeyInvalid_When_TheKeyIsNotInTheConversationsFolder(string? key)
    {
        // Act
        var result = Message.Photo(ConversationId, ConversationParty.Host, key, caption: null, bookingId: null, Now);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be(ConversationsErrors.ImageKeyInvalid);
    }

    [Fact]
    public void Photo_Should_ReturnImageKeyTooLong_When_TheKeyIsLongerThanTheLimit()
    {
        // Arrange
        var key = $"conversations/{new string('a', Message.ImageKeyMaxLength)}.webp";

        // Act
        var result = Message.Photo(ConversationId, ConversationParty.Host, key, caption: null, bookingId: null, Now);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ImageKeyTooLong);
    }

    [Fact]
    public void Photo_Should_UseTheGivenId_When_OneIsPassed()
    {
        // Arrange
        var id = new MessageId(Guid.CreateVersion7());

        // Act
        var result = Message.Photo(ConversationId, ConversationParty.Host, PhotoKey, caption: null, bookingId: null, Now, id);

        // Assert
        result.Value!.Id.Should().Be(id);
    }

    [Fact]
    public void Photo_Should_ReturnBothErrors_When_KeyAndCaptionAreInvalid()
    {
        // Arrange
        var caption = new string('a', Message.BodyMaxLength + 1);

        // Act
        var result = Message.Photo(ConversationId, ConversationParty.Host, "javascript:alert(1)", caption, bookingId: null, Now);

        // Assert
        result.Errors.Should().BeEquivalentTo([ConversationsErrors.ImageKeyInvalid, ConversationsErrors.BodyTooLong]);
    }
}
