using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Conversations;

/// <summary>Bloqueio da conversa: qualquer lado bloqueia, só quem bloqueou desbloqueia.</summary>
public sealed class ConversationBlockTests
{
    private static readonly DateTimeOffset Later = ConversationBuilder.DefaultNow.AddHours(2);

    [Theory]
    [InlineData(ConversationParty.Owner)]
    [InlineData(ConversationParty.Host)]
    public void Block_Should_BlockTheConversationByThatSide_When_ItIsOpen(ConversationParty party)
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();

        // Act
        var result = conversation.Block(party, Later);

        // Assert
        result.Value.Should().BeTrue();
        conversation.IsBlocked.Should().BeTrue();
        conversation.BlockedBy.Should().Be(party);
        conversation.BlockedAt.Should().Be(Later);
        conversation.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void Block_Should_ChangeNothing_When_TheSameSideBlocksAgain()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.Block(ConversationParty.Host, Later);

        // Act
        var result = conversation.Block(ConversationParty.Host, Later.AddHours(1));

        // Assert
        result.Value.Should().BeFalse();
        conversation.BlockedAt.Should().Be(Later, "o bloqueio original continua valendo");
    }

    [Fact]
    public void Block_Should_Fail_When_TheOtherSideAlreadyBlocked()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.Block(ConversationParty.Host, Later);

        // Act
        var result = conversation.Block(ConversationParty.Owner, Later.AddHours(1));

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.AlreadyBlocked);
        conversation.BlockedBy.Should().Be(ConversationParty.Host);
    }

    [Fact]
    public void Unblock_Should_OpenTheConversation_When_TheBlockerUnblocks()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.Block(ConversationParty.Owner, Later);

        // Act
        var result = conversation.Unblock(ConversationParty.Owner, Later.AddHours(1));

        // Assert
        result.Value.Should().BeTrue();
        conversation.IsBlocked.Should().BeFalse();
        conversation.BlockedBy.Should().BeNull();
        conversation.BlockedAt.Should().BeNull();
        conversation.UpdatedAt.Should().Be(Later.AddHours(1));
    }

    [Fact]
    public void Unblock_Should_Fail_When_TheOtherSideBlocked()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.Block(ConversationParty.Host, Later);

        // Act
        var result = conversation.Unblock(ConversationParty.Owner, Later.AddHours(1));

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.UnblockForbidden);
        conversation.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void Unblock_Should_ChangeNothing_When_TheConversationIsOpen()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();

        // Act
        var result = conversation.Unblock(ConversationParty.Owner, Later);

        // Assert
        result.Value.Should().BeFalse();
        conversation.UpdatedAt.Should().Be(ConversationBuilder.DefaultNow);
    }

    [Fact]
    public void BlockByModeration_Should_BlockTheConversationWithoutASide_When_ItIsOpen()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();

        // Act
        var changed = conversation.BlockByModeration(Later);

        // Assert
        changed.Should().BeTrue();
        conversation.IsBlocked.Should().BeTrue();
        conversation.IsBlockedByModeration.Should().BeTrue();
        conversation.ModerationBlockedAt.Should().Be(Later);
        conversation.BlockedBy.Should().BeNull();
        conversation.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void BlockByModeration_Should_ChangeNothing_When_ModerationAlreadyBlocked()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.BlockByModeration(Later);

        // Act
        var changed = conversation.BlockByModeration(Later.AddDays(1));

        // Assert
        changed.Should().BeFalse();
        conversation.ModerationBlockedAt.Should().Be(Later);
    }

    [Fact]
    public void Unblock_Should_KeepTheModerationBlock_When_TheSideThatBlockedUnblocks()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.Block(ConversationParty.Owner, Later);
        conversation.BlockByModeration(Later.AddHours(1));

        // Act
        var result = conversation.Unblock(ConversationParty.Owner, Later.AddHours(2));

        // Assert
        result.Value.Should().BeTrue();
        conversation.BlockedBy.Should().BeNull();
        conversation.IsBlocked.Should().BeTrue("o bloqueio da moderação nenhum dos lados tira");
    }

    [Fact]
    public void Block_Should_StillWorkForASide_When_ModerationAlreadyBlocked()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.BlockByModeration(Later);

        // Act
        var result = conversation.Block(ConversationParty.Host, Later.AddHours(1));

        // Assert
        result.Value.Should().BeTrue();
        conversation.BlockedBy.Should().Be(ConversationParty.Host);
    }

    [Fact]
    public void LiftModerationBlock_Should_OpenTheConversation_When_OnlyModerationBlocked()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.BlockByModeration(Later);

        // Act
        var changed = conversation.LiftModerationBlock(Later.AddDays(1));

        // Assert
        changed.Should().BeTrue();
        conversation.IsBlocked.Should().BeFalse();
        conversation.ModerationBlockedAt.Should().BeNull();
        conversation.UpdatedAt.Should().Be(Later.AddDays(1));
    }

    [Fact]
    public void LiftModerationBlock_Should_KeepTheSideBlock_When_ASideAlsoBlocked()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.Block(ConversationParty.Host, Later);
        conversation.BlockByModeration(Later.AddHours(1));

        // Act
        conversation.LiftModerationBlock(Later.AddDays(1));

        // Assert
        conversation.IsBlocked.Should().BeTrue();
        conversation.BlockedBy.Should().Be(ConversationParty.Host);
    }

    [Fact]
    public void LiftModerationBlock_Should_ChangeNothing_When_ModerationDidNotBlock()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();

        // Act
        var changed = conversation.LiftModerationBlock(Later);

        // Assert
        changed.Should().BeFalse();
        conversation.UpdatedAt.Should().Be(ConversationBuilder.DefaultNow);
    }

    [Fact]
    public void Block_Should_NotMoveUpdatedAtBack_When_TheClockIsBehind()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.RecordMessage(Later);

        // Act
        conversation.Block(ConversationParty.Owner, Later.AddMinutes(-5));

        // Assert
        conversation.UpdatedAt.Should().Be(Later);
    }
}
