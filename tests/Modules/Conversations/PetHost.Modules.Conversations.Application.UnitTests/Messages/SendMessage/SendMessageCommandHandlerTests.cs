using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Application.Messages.SendMessage;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Messages.SendMessage;

public sealed class SendMessageCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddMinutes(30);

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IMessageRepository> _messages = new(MockBehavior.Strict);
    private readonly Mock<IConversationsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationBookings> _bookings = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationRealtime> _realtime = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly SendMessageCommandHandler _sut;

    private Message? _added;
    private bool _locked;
    private bool _savedWhileLocked;

    public SendMessageCommandHandlerTests()
    {
        _sut = new SendMessageCommandHandler(
            _repository.Object,
            _messages.Object,
            _unitOfWork.Object,
            _parties.Object,
            _bookings.Object,
            new ConversationNotifications(_parties.Object, _queries.Object, _realtime.Object),
            _auditTrail.Object,
            new FakeTimeProvider(Now));

        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableHost);
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
        _parties.Setup(p => p.IsSuspendedAsync(It.IsAny<ConversationParty>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _repository.Setup(r => r.ReloadAsync(_conversation, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _bookings.Setup(b => b.GetInProgressBookingIdAsync(ConversationBuilder.OwnerId, ConversationBuilder.HostId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        _messages.Setup(m => m.Add(It.IsAny<Message>())).Callback<Message>(message => _added = message);
        _unitOfWork
            .Setup(u => u.RunLockedOnConversationAsync(It.IsAny<ConversationId>(), It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<ConversationId, Func<CancellationToken, Task<Result>>, CancellationToken>(async (_, work, ct) =>
            {
                _locked = true;
                var result = await work(ct);
                _locked = false;
                return result;
            });
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => _savedWhileLocked = _locked).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _queries.Setup(q => q.CountUnreadAsync(It.IsAny<ConversationParticipant>(), It.IsAny<CancellationToken>())).ReturnsAsync(5);
        _realtime.Setup(r => r.MessageCreatedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<MessageResponse>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _realtime.Setup(r => r.UnreadCountChangedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveTheMessageAndRecordIt_When_TheOwnerSends()
    {
        // Act
        var result = await _sut.HandleAsync(OwnerSends("  A Pipoca tem medo de fogos?  "), CancellationToken.None);

        // Assert
        var response = result.Value!;
        response.Id.Should().Be(_added!.Id.Value);
        response.ConversationId.Should().Be(_conversation.Id.Value);
        response.SentBy.Should().Be("owner");
        response.Body.Should().Be("A Pipoca tem medo de fogos?");
        response.CreatedAt.Should().Be(Now);
        _conversation.LastMessageAt.Should().Be(Now);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.ConversationMessageSent
                && r.TargetType == AuditTargets.Conversation
                && r.TargetId == _conversation.Id.Value
                && r.Details!["messageId"] == _added.Id.Value.ToString()
                && r.Details["hasPhoto"] == "false"
                && !r.Details.Values.Contains("A Pipoca tem medo de fogos?")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveInsideTheConversationLockAfterReloading_When_TheOwnerSends()
    {
        // Act
        await _sut.HandleAsync(OwnerSends("Oi!"), CancellationToken.None);

        // Assert
        _unitOfWork.Verify(u => u.RunLockedOnConversationAsync(_conversation.Id, It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.ReloadAsync(_conversation, It.IsAny<CancellationToken>()), Times.Once);
        _savedWhileLocked.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_KeepTheNewerLastActivity_When_AnotherMessageWasSavedWhileWaitingForTheLock()
    {
        // Arrange
        var newer = Now.AddSeconds(1);
        _repository
            .Setup(r => r.ReloadAsync(_conversation, It.IsAny<CancellationToken>()))
            .Callback(() => _conversation.RecordMessage(newer))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.HandleAsync(OwnerSends("Oi!"), CancellationToken.None);

        // Assert
        _conversation.LastMessageAt.Should().Be(newer);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnRecipientSuspendedWithoutSaving_When_TheOwnerWritesToASuspendedHost()
    {
        // Arrange
        _parties.Setup(p => p.IsSuspendedAsync(ConversationParty.Host, ConversationBuilder.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAsync(OwnerSends("Oi"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.RecipientSuspended);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(r => r.MessageCreatedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<MessageResponse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnRecipientSuspendedWithoutSaving_When_TheHostWritesToASuspendedOwner()
    {
        // Arrange
        _parties.Setup(p => p.IsSuspendedAsync(ConversationParty.Owner, ConversationBuilder.OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAsync(new SendMessageCommand(ConversationBuilder.HostUserId, Roles.Host, _conversation.Id.Value, "Oi"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.RecipientSuspended);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_PushTheMessageToBothAccountsAndTheCountToTheHost_When_TheOwnerSends()
    {
        // Act
        await _sut.HandleAsync(OwnerSends("Oi!"), CancellationToken.None);

        // Assert
        _realtime.Verify(r => r.MessageCreatedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(ConversationBuilder.OwnerUserId) && ids.Contains(ConversationBuilder.HostUserId)),
            It.Is<MessageResponse>(m => m.Body == "Oi!"),
            It.IsAny<CancellationToken>()), Times.Once);
        _queries.Verify(q => q.CountUnreadAsync(new ConversationParticipant(ConversationParty.Host, ConversationBuilder.HostId), It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.UnreadCountChangedAsync(ConversationBuilder.HostUserId, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SendAsTheHost_When_TheRoleIsHost()
    {
        // Act
        var result = await _sut.HandleAsync(new SendMessageCommand(ConversationBuilder.HostUserId, Roles.Host, _conversation.Id.Value, "Pode trazer!"), CancellationToken.None);

        // Assert
        result.Value!.SentBy.Should().Be("host");
        _realtime.Verify(r => r.UnreadCountChangedAsync(ConversationBuilder.OwnerUserId, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_LinkTheMessageToTheStay_When_ThePairHasABookingInProgress()
    {
        // Arrange
        _bookings
            .Setup(b => b.GetInProgressBookingIdAsync(ConversationBuilder.OwnerId, ConversationBuilder.HostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationBuilder.BookingId);

        // Act
        var result = await _sut.HandleAsync(OwnerSends("Como ela está?"), CancellationToken.None);

        // Assert
        result.Value!.BookingId.Should().Be(ConversationBuilder.BookingId);
        _added!.BookingId.Should().Be(ConversationBuilder.BookingId);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostDeactivatedWithoutSaving_When_TheHostIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.SuspendedHost);

        // Act
        var result = await _sut.HandleAsync(new SendMessageCommand(ConversationBuilder.HostUserId, Roles.Host, _conversation.Id.Value, "Oi"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.HostDeactivated);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFoundWithoutSaving_When_TheConversationIsFromAnotherOwner()
    {
        // Arrange
        var someoneElses = new ConversationBuilder().WithOwner(TestIds.Of(77)).Build();
        _repository.Setup(r => r.GetByIdAsync(someoneElses.Id, It.IsAny<CancellationToken>())).ReturnsAsync(someoneElses);

        // Act
        var result = await _sut.HandleAsync(new SendMessageCommand(ConversationBuilder.OwnerUserId, Roles.Owner, someoneElses.Id.Value, "Oi"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(someoneElses.Id));
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBodyRequiredWithoutSaving_When_TheTextIsBlank()
    {
        // Act
        var result = await _sut.HandleAsync(OwnerSends("   "), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.BodyRequired);
        _messages.Verify(m => m.Add(It.IsAny<Message>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotRecordNorPush_When_SavingFails()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ConversationsErrors.Conflict));

        // Act
        var result = await _sut.HandleAsync(OwnerSends("Oi"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(r => r.MessageCreatedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<MessageResponse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private SendMessageCommand OwnerSends(string body) =>
        new(ConversationBuilder.OwnerUserId, Roles.Owner, _conversation.Id.Value, body);
}
