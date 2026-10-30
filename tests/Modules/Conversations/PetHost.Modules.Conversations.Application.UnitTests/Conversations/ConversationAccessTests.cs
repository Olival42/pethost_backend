using FluentAssertions;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations;

public sealed class ConversationAccessTests
{
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();

    [Fact]
    public async Task ResolveParticipantAsync_Should_ReturnTheOwnerSide_When_RoleIsOwnerAndTheAccountHasTheProfile()
    {
        // Arrange
        GivenOwner(ConversationsTestData.AvailableOwner);

        // Act
        var result = await ConversationAccess.ResolveParticipantAsync(_parties.Object, ConversationBuilder.OwnerUserId, Roles.Owner, requireAvailable: true, CancellationToken.None);

        // Assert
        result.Value.Should().Be(new ConversationParticipant(ConversationParty.Owner, ConversationBuilder.OwnerId));
    }

    [Fact]
    public async Task ResolveParticipantAsync_Should_ReturnTheHostSide_When_RoleIsHostAndTheAccountHasTheProfile()
    {
        // Arrange
        GivenHost(ConversationsTestData.AvailableHost);

        // Act
        var result = await ConversationAccess.ResolveParticipantAsync(_parties.Object, ConversationBuilder.HostUserId, Roles.Host, requireAvailable: true, CancellationToken.None);

        // Assert
        result.Value.Should().Be(new ConversationParticipant(ConversationParty.Host, ConversationBuilder.HostId));
    }

    [Fact]
    public async Task ResolveParticipantAsync_Should_ReturnOwnerNotFound_When_TheAccountHasNoOwnerProfile()
    {
        // Arrange
        GivenOwner(null);

        // Act
        var result = await ConversationAccess.ResolveParticipantAsync(_parties.Object, ConversationBuilder.OwnerUserId, Roles.Owner, requireAvailable: false, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.OwnerNotFound);
    }

    [Fact]
    public async Task ResolveParticipantAsync_Should_ReturnHostNotFound_When_TheAccountHasNoHostProfile()
    {
        // Arrange
        GivenHost(null);

        // Act
        var result = await ConversationAccess.ResolveParticipantAsync(_parties.Object, ConversationBuilder.HostUserId, Roles.Host, requireAvailable: false, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.HostNotFound);
    }

    [Fact]
    public async Task ResolveParticipantAsync_Should_ReturnOwnerDeactivated_When_AvailabilityIsRequiredAndTheOwnerIsSuspended()
    {
        // Arrange
        GivenOwner(ConversationsTestData.SuspendedOwner);

        // Act
        var result = await ConversationAccess.ResolveParticipantAsync(_parties.Object, ConversationBuilder.OwnerUserId, Roles.Owner, requireAvailable: true, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.OwnerDeactivated);
    }

    [Fact]
    public async Task ResolveParticipantAsync_Should_ReturnHostDeactivated_When_AvailabilityIsRequiredAndTheHostIsSuspended()
    {
        // Arrange
        GivenHost(ConversationsTestData.SuspendedHost);

        // Act
        var result = await ConversationAccess.ResolveParticipantAsync(_parties.Object, ConversationBuilder.HostUserId, Roles.Host, requireAvailable: true, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.HostDeactivated);
    }

    [Fact]
    public async Task ResolveParticipantAsync_Should_AcceptASuspendedProfile_When_AvailabilityIsNotRequired()
    {
        // Arrange
        GivenOwner(ConversationsTestData.SuspendedOwner);

        // Act
        var result = await ConversationAccess.ResolveParticipantAsync(_parties.Object, ConversationBuilder.OwnerUserId, Roles.Owner, requireAvailable: false, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnTheConversationAndTheSide_When_TheRequesterIsTheOwner()
    {
        // Arrange
        GivenOwner(ConversationsTestData.AvailableOwner);
        GivenConversation(_conversation);

        // Act
        var result = await ConversationAccess.LoadAsync(_repository.Object, _parties.Object, ConversationBuilder.OwnerUserId, Roles.Owner, _conversation.Id.Value, requireAvailable: true, CancellationToken.None);

        // Assert
        result.Value!.Conversation.Should().BeSameAs(_conversation);
        result.Value.Participant.Side.Should().Be(ConversationParty.Owner);
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnNotFound_When_TheConversationDoesNotExist()
    {
        // Arrange
        GivenOwner(ConversationsTestData.AvailableOwner);
        GivenConversation(null);

        // Act
        var result = await ConversationAccess.LoadAsync(_repository.Object, _parties.Object, ConversationBuilder.OwnerUserId, Roles.Owner, _conversation.Id.Value, requireAvailable: false, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(_conversation.Id));
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnNotFound_When_TheConversationIsFromAnotherOwner()
    {
        // Arrange
        var someoneElses = new ConversationBuilder().WithOwner(TestIds.Of(77)).Build();
        GivenOwner(ConversationsTestData.AvailableOwner);
        GivenConversation(someoneElses);

        // Act
        var result = await ConversationAccess.LoadAsync(_repository.Object, _parties.Object, ConversationBuilder.OwnerUserId, Roles.Owner, someoneElses.Id.Value, requireAvailable: false, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(someoneElses.Id));
    }

    [Fact]
    public async Task LoadAsync_Should_ReturnNotFound_When_TheHostIsNotTheHostOfTheConversation()
    {
        // Arrange
        var otherHost = new ConversationBuilder().WithHost(TestIds.Of(88)).Build();
        GivenHost(ConversationsTestData.AvailableHost);
        GivenConversation(otherHost);

        // Act
        var result = await ConversationAccess.LoadAsync(_repository.Object, _parties.Object, ConversationBuilder.HostUserId, Roles.Host, otherHost.Id.Value, requireAvailable: false, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(otherHost.Id));
    }

    [Fact]
    public async Task LoadAsync_Should_NotReadTheConversation_When_TheRequesterHasNoProfile()
    {
        // Arrange
        GivenOwner(null);

        // Act
        var result = await ConversationAccess.LoadAsync(_repository.Object, _parties.Object, ConversationBuilder.OwnerUserId, Roles.Owner, _conversation.Id.Value, requireAvailable: false, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.OwnerNotFound);
        _repository.Verify(r => r.GetByIdAsync(It.IsAny<ConversationId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureRecipientNotSuspendedAsync_Should_CheckTheHostAndPass_When_TheOwnerWritesToAnActiveHost()
    {
        // Arrange
        _parties.Setup(p => p.IsSuspendedAsync(ConversationParty.Host, ConversationBuilder.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var result = await ConversationAccess.EnsureRecipientNotSuspendedAsync(_parties.Object, _conversation, ConversationParty.Owner, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _parties.Verify(p => p.IsSuspendedAsync(ConversationParty.Host, ConversationBuilder.HostId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureRecipientNotSuspendedAsync_Should_ReturnRecipientSuspended_When_TheHostWritesToASuspendedOwner()
    {
        // Arrange
        _parties.Setup(p => p.IsSuspendedAsync(ConversationParty.Owner, ConversationBuilder.OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await ConversationAccess.EnsureRecipientNotSuspendedAsync(_parties.Object, _conversation, ConversationParty.Host, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.RecipientSuspended);
    }

    [Theory]
    [InlineData(nameof(ConversationsErrors.OwnerNotFound), true)]
    [InlineData(nameof(ConversationsErrors.HostNotFound), true)]
    [InlineData(nameof(ConversationsErrors.OwnerDeactivated), false)]
    [InlineData(nameof(ConversationsErrors.HostDeactivated), false)]
    public void IsMissingProfile_Should_BeTrueOnlyForTheProfileNotFoundErrors_When_ResolvingFails(string error, bool expected)
    {
        // Arrange
        var failure = Result.Failure((Error)typeof(ConversationsErrors).GetField(error)!.GetValue(null)!);

        // Act
        var missing = ConversationAccess.IsMissingProfile(failure);

        // Assert
        missing.Should().Be(expected);
    }

    [Fact]
    public void IsMissingProfile_Should_BeFalse_When_TheProfileWasResolved()
    {
        // Act
        var missing = ConversationAccess.IsMissingProfile(Result.Success());

        // Assert
        missing.Should().BeFalse();
    }

    private void GivenOwner(PartyProfile? owner) =>
        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);

    private void GivenHost(PartyProfile? host) =>
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(host);

    private void GivenConversation(Conversation? conversation) =>
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<ConversationId>(), It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
}
