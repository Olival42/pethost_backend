using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Conversations;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow;

    [Fact]
    public void Start_Should_CreateConversationBetweenTheOwnerAndTheHost_When_IdsAreValid()
    {
        // Act
        var conversation = Conversation.Start(ConversationBuilder.OwnerId, ConversationBuilder.HostId, Now);

        // Assert
        conversation.Id.Value.Should().NotBeEmpty();
        conversation.OwnerId.Should().Be(ConversationBuilder.OwnerId);
        conversation.HostId.Should().Be(ConversationBuilder.HostId);
        conversation.LastMessageAt.Should().BeNull();
        conversation.CreatedAt.Should().Be(Now);
        conversation.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Start_Should_Throw_When_OwnerIdIsEmpty()
    {
        // Act
        var start = () => Conversation.Start(Guid.Empty, ConversationBuilder.HostId, Now);

        // Assert
        start.Should().Throw<ArgumentException>().WithParameterName("ownerId");
    }

    [Fact]
    public void Start_Should_Throw_When_HostIdIsEmpty()
    {
        // Act
        var start = () => Conversation.Start(ConversationBuilder.OwnerId, Guid.Empty, Now);

        // Assert
        start.Should().Throw<ArgumentException>().WithParameterName("hostId");
    }

    [Fact]
    public void Start_Should_GenerateDifferentIds_When_CalledTwice()
    {
        // Act
        var first = new ConversationBuilder().Build();
        var second = new ConversationBuilder().Build();

        // Assert
        first.Id.Should().NotBe(second.Id);
    }

    [Theory]
    [InlineData(ConversationParty.Owner, "0199c000-0000-7000-8000-000000000001", true)]
    [InlineData(ConversationParty.Host, "0199c000-0000-7000-8000-000000000002", true)]
    [InlineData(ConversationParty.Owner, "0199c000-0000-7000-8000-000000000002", false)]
    [InlineData(ConversationParty.Host, "0199c000-0000-7000-8000-000000000001", false)]
    [InlineData(ConversationParty.Owner, "0199c000-0000-7000-8000-000000000099", false)]
    public void HasParticipant_Should_MatchOnlyTheProfileOfThatSide_When_Checked(ConversationParty side, string profileId, bool expected)
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();

        // Act
        var result = conversation.HasParticipant(side, Guid.Parse(profileId));

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void RecordMessage_Should_SetLastMessageAtAndUpdatedAt_When_ItIsTheFirstMessage()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        var sentAt = Now.AddMinutes(5);

        // Act
        conversation.RecordMessage(sentAt);

        // Assert
        conversation.LastMessageAt.Should().Be(sentAt);
        conversation.UpdatedAt.Should().Be(sentAt);
    }

    [Fact]
    public void RecordMessage_Should_KeepTheLatestInstant_When_AnOlderMessageIsRecordedAfterANewerOne()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.RecordMessage(Now.AddMinutes(10));

        // Act
        conversation.RecordMessage(Now.AddMinutes(3));

        // Assert
        conversation.LastMessageAt.Should().Be(Now.AddMinutes(10));
        conversation.UpdatedAt.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public void LastActivityAt_Should_BeTheOpening_When_NobodyWroteYet()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();

        // Act
        var lastActivity = conversation.LastActivityAt;

        // Assert
        lastActivity.Should().Be(Now);
    }

    [Fact]
    public void LastActivityAt_Should_BeTheLastMessage_When_SomebodyWrote()
    {
        // Arrange
        var conversation = new ConversationBuilder().Build();
        conversation.RecordMessage(Now.AddDays(2));

        // Act
        var lastActivity = conversation.LastActivityAt;

        // Assert
        lastActivity.Should().Be(Now.AddDays(2));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void IsInactiveSince_Should_BeTrueOnlyBeforeTheCutoff_When_LastActivityIsCompared(int lastActivityOffsetDays, bool expected)
    {
        // Arrange
        var cutoff = Now.AddDays(90);
        var conversation = new ConversationBuilder().Build();
        conversation.RecordMessage(cutoff.AddDays(lastActivityOffsetDays));

        // Act
        var inactive = conversation.IsInactiveSince(cutoff);

        // Assert
        inactive.Should().Be(expected);
    }
}
