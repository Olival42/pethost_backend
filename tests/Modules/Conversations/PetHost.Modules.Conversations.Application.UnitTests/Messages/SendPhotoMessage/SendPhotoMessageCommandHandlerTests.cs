using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Messages.SendPhotoMessage;

public sealed class SendPhotoMessageCommandHandlerTests
{
    private const string PhotoUrl = "https://images.pethost.local/conversations/pipoca.webp";

    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddDays(9);
    private static readonly ImageUpload Image = new(Stream.Null, "pipoca.webp", "image/webp");

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IMessageRepository> _messages = new(MockBehavior.Strict);
    private readonly Mock<IConversationsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationBookings> _bookings = new(MockBehavior.Strict);
    private readonly Mock<IImageStorage> _storage = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationRealtime> _realtime = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly SendPhotoMessageCommandHandler _sut;

    public SendPhotoMessageCommandHandlerTests()
    {
        _sut = new SendPhotoMessageCommandHandler(
            _repository.Object,
            _messages.Object,
            _unitOfWork.Object,
            _parties.Object,
            _bookings.Object,
            _storage.Object,
            new ConversationNotifications(_parties.Object, _queries.Object, _realtime.Object),
            _auditTrail.Object,
            new FakeTimeProvider(Now));

        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableHost);
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
        _parties.Setup(p => p.IsSuspendedAsync(It.IsAny<ConversationParty>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _repository.Setup(r => r.ReloadAsync(_conversation, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.RunLockedOnConversationAsync(It.IsAny<ConversationId>(), It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<ConversationId, Func<CancellationToken, Task<Result>>, CancellationToken>((_, work, ct) => work(ct));
        _bookings.Setup(b => b.GetInProgressBookingIdAsync(ConversationBuilder.OwnerId, ConversationBuilder.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationBuilder.BookingId);
        _storage.Setup(s => s.SaveAsync(Image, ImageFolders.Conversations, It.IsAny<CancellationToken>())).ReturnsAsync(Result<StoredImage>.Success(new StoredImage(PhotoUrl, "hash")));
        _storage.Setup(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _messages.Setup(m => m.Add(It.IsAny<Message>()));
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _queries.Setup(q => q.CountUnreadAsync(It.IsAny<ConversationParticipant>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _realtime.Setup(r => r.MessageCreatedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<MessageResponse>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _realtime.Setup(r => r.UnreadCountChangedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_Should_UploadSaveRecordAndPush_When_TheHostSendsAPhotoDuringTheStay()
    {
        // Act
        var result = await _sut.HandleAsync(HostSends("Brincando no quintal"), CancellationToken.None);

        // Assert
        var response = result.Value!;
        response.ImageUrl.Should().Be(PhotoUrl);
        response.Body.Should().Be("Brincando no quintal");
        response.SentBy.Should().Be("host");
        response.BookingId.Should().Be(ConversationBuilder.BookingId);
        _conversation.LastMessageAt.Should().Be(Now);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.ConversationMessageSent && r.Details!["hasPhoto"] == "true" && !r.Details.Values.Contains(PhotoUrl)),
            It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.UnreadCountChangedAsync(ConversationBuilder.OwnerUserId, 1, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(PhotoUrl, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFoundWithoutUploading_When_TheConversationIsFromAnotherHost()
    {
        // Arrange
        var otherHost = new ConversationBuilder().WithHost(TestIds.Of(88)).Build();
        _repository.Setup(r => r.GetByIdAsync(otherHost.Id, It.IsAny<CancellationToken>())).ReturnsAsync(otherHost);

        // Act
        var result = await _sut.HandleAsync(new SendPhotoMessageCommand(ConversationBuilder.HostUserId, Roles.Host, otherHost.Id.Value, Image, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(otherHost.Id));
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostDeactivatedWithoutUploading_When_TheHostIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.SuspendedHost);

        // Act
        var result = await _sut.HandleAsync(HostSends(null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.HostDeactivated);
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnRecipientSuspendedWithoutUploading_When_TheOwnerIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.IsSuspendedAsync(ConversationParty.Owner, ConversationBuilder.OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAsync(HostSends("Olha ela!"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.RecipientSuspended);
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveInsideTheConversationLockAfterReloading_When_TheHostSendsAPhoto()
    {
        // Act
        await _sut.HandleAsync(HostSends(null), CancellationToken.None);

        // Assert
        _unitOfWork.Verify(u => u.RunLockedOnConversationAsync(_conversation.Id, It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.ReloadAsync(_conversation, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTheFileErrorWithoutSaving_When_TheBucketRefusesTheImage()
    {
        // Arrange
        _storage.Setup(s => s.SaveAsync(Image, ImageFolders.Conversations, It.IsAny<CancellationToken>())).ReturnsAsync(Result<StoredImage>.Failure(ImageErrors.FileRequired));

        // Act
        var result = await _sut.HandleAsync(HostSends(null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ImageErrors.FileRequired);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_DeleteTheUploadedPhotoAndNotPush_When_SavingFails()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ConversationsErrors.Conflict));

        // Act
        var result = await _sut.HandleAsync(HostSends(null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.Conflict);
        _storage.Verify(s => s.DeleteAsync(PhotoUrl, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(r => r.MessageCreatedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<MessageResponse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_DeleteTheUploadedPhoto_When_TheCaptionIsTooLong()
    {
        // Act
        var result = await _sut.HandleAsync(HostSends(new string('a', Message.BodyMaxLength + 1)), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.BodyTooLong);
        _storage.Verify(s => s.DeleteAsync(PhotoUrl, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBlockedWithoutUploading_When_TheConversationIsBlocked()
    {
        // Arrange
        _conversation.Block(ConversationParty.Owner, ConversationBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(HostSends("Olha ela!"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.Blocked);
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private SendPhotoMessageCommand HostSends(string? caption) =>
        new(ConversationBuilder.HostUserId, Roles.Host, _conversation.Id.Value, Image, caption);
}
