using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Conversations.StartConversation;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.StartConversation;

public sealed class StartConversationCommandHandlerTests
{
    private static readonly StartConversationCommand Command = new(ConversationBuilder.OwnerUserId, ConversationBuilder.ListingId);

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IConversationsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationListings> _listings = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly StartConversationCommandHandler _sut;

    private Conversation? _added;

    public StartConversationCommandHandlerTests()
    {
        _sut = new StartConversationCommandHandler(
            _repository.Object,
            _unitOfWork.Object,
            _parties.Object,
            _listings.Object,
            _queries.Object,
            new ConversationResponseBuilder(_parties.Object),
            _auditTrail.Object,
            new FakeTimeProvider(ConversationBuilder.DefaultNow));

        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
        _listings.Setup(l => l.FindListableHostIdAsync(ConversationBuilder.ListingId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationBuilder.HostId);
        GivenContacts(ownerEmail: "camila@pethost.test", hostEmail: "cida@pethost.test");
        _repository.Setup(r => r.Add(It.IsAny<Conversation>())).Callback<Conversation>(c => _added = c);
        _queries
            .Setup(q => q.GetAsync(It.IsAny<ConversationId>(), ConversationParty.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationId id, ConversationParty _, CancellationToken _) => ConversationsTestData.Row(_added ?? new ConversationBuilder().Build()) with { Id = id });
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_Should_CreateTheConversationAndRecordIt_When_ThePairHasNoConversation()
    {
        // Arrange
        GivenExisting(null);
        GivenSave(Result.Success());

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.Created.Should().BeTrue();
        result.Value.Conversation.Id.Should().Be(_added!.Id.Value);
        result.Value.Conversation.OwnerId.Should().Be(ConversationBuilder.OwnerId);
        result.Value.Conversation.HostId.Should().Be(ConversationBuilder.HostId);
        result.Value.Conversation.Owner!.Name.Should().Be("Camila Souza");
        result.Value.Conversation.Host!.Name.Should().Be("Dona Cida");
        _added.CreatedAt.Should().Be(ConversationBuilder.DefaultNow);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ConversationStarted && r.TargetType == AuditTargets.Conversation && r.TargetId == _added.Id.Value), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTheExistingConversationWithoutSaving_When_ThePairAlreadyHasOne()
    {
        // Arrange
        var existing = new ConversationBuilder().Build();
        GivenExisting(existing);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.Created.Should().BeFalse();
        result.Value.Conversation.Id.Should().Be(existing.Id.Value);
        _repository.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTheWinner_When_AnotherRequestOpenedTheSameConversationAtTheSameTime()
    {
        // Arrange
        var winner = new ConversationBuilder().Build();
        _repository
            .SetupSequence(r => r.FindByPartiesAsync(ConversationBuilder.OwnerId, ConversationBuilder.HostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null)
            .ReturnsAsync(winner);
        GivenSave(Result.Failure(ConversationsErrors.AlreadyExists));

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.Created.Should().BeFalse();
        result.Value.Conversation.Id.Should().Be(winner.Id.Value);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTheSaveError_When_SavingFailsForAnotherReason()
    {
        // Arrange
        GivenExisting(null);
        GivenSave(Result.Failure(ConversationsErrors.Conflict));

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnListingNotFound_When_TheListingIsNotListable()
    {
        // Arrange
        _listings.Setup(l => l.FindListableHostIdAsync(ConversationBuilder.ListingId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ListingNotFound(ConversationBuilder.ListingId));
        _repository.Verify(r => r.FindByPartiesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerNotFound_When_TheAccountHasNoOwnerProfile()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync((PartyProfile?)null);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.OwnerNotFound);
        _listings.Verify(l => l.FindListableHostIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerDeactivated_When_TheOwnerIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.SuspendedOwner);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.OwnerDeactivated);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnListing_When_TheHostAccountIsTheSamePerson()
    {
        // Arrange: a conta de anfitrião do cantinho tem o mesmo e-mail da tutora (maiúsculas não importam).
        GivenContacts(ownerEmail: "camila@pethost.test", hostEmail: "CAMILA@pethost.test");

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.OwnListing);
        _repository.Verify(r => r.Add(It.IsAny<Conversation>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private void GivenContacts(string ownerEmail, string hostEmail)
    {
        _parties.Setup(p => p.GetContactsAsync(ConversationParty.Owner, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PartyContact>
            {
                [ConversationBuilder.OwnerId] = new(ConversationBuilder.OwnerId, ConversationBuilder.OwnerUserId, "Camila Souza", ownerEmail, CanBeNotified: true),
            });
        _parties.Setup(p => p.GetContactsAsync(ConversationParty.Host, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PartyContact>
            {
                [ConversationBuilder.HostId] = new(ConversationBuilder.HostId, Guid.CreateVersion7(), "Dona Cida", hostEmail, CanBeNotified: true),
            });
    }

    private void GivenExisting(Conversation? existing) =>
        _repository.Setup(r => r.FindByPartiesAsync(ConversationBuilder.OwnerId, ConversationBuilder.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

    private void GivenSave(Result result) =>
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(result);
}
