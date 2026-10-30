using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Preferences.GetNotificationSettings;
using PetHost.Modules.Conversations.Application.Preferences.UpdateNotificationSettings;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Preferences;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Preferences;

/// <summary>Ler, ligar e desligar o e-mail de mensagens não lidas.</summary>
public sealed class NotificationSettingsTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddDays(1);
    private static readonly Guid UserId = ConversationBuilder.HostUserId;

    private readonly Mock<INotificationPreferenceRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IConversationsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);

    private NotificationPreference? _added;

    public NotificationSettingsTests()
    {
        _repository.Setup(r => r.Add(It.IsAny<NotificationPreference>())).Callback<NotificationPreference>(p => _added = p);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
    }

    private UpdateNotificationSettingsCommandHandler Update => new(_repository.Object, _unitOfWork.Object, new FakeTimeProvider(Now));

    [Fact]
    public async Task Get_Should_ReturnEnabled_When_TheAccountNeverChanged()
    {
        // Arrange
        GivenStored(null);

        // Act
        var result = await new GetNotificationSettingsQueryHandler(_repository.Object).HandleAsync(new GetNotificationSettingsQuery(UserId), CancellationToken.None);

        // Assert
        result.Value!.UnreadMessagesEmail.Should().BeTrue();
    }

    [Fact]
    public async Task Get_Should_ReturnTheStoredChoice_When_TheAccountTurnedItOff()
    {
        // Arrange
        GivenStored(NotificationPreference.Create(UserId, unreadMessagesEmail: false, ConversationBuilder.DefaultNow));

        // Act
        var result = await new GetNotificationSettingsQueryHandler(_repository.Object).HandleAsync(new GetNotificationSettingsQuery(UserId), CancellationToken.None);

        // Assert
        result.Value!.UnreadMessagesEmail.Should().BeFalse();
    }

    [Fact]
    public async Task Update_Should_CreateTheChoice_When_TheAccountTurnsItOffForTheFirstTime()
    {
        // Arrange
        GivenStored(null);

        // Act
        var result = await Update.HandleAsync(new UpdateNotificationSettingsCommand(UserId, false), CancellationToken.None);

        // Assert
        result.Value!.UnreadMessagesEmail.Should().BeFalse();
        _added!.Id.Should().Be(UserId);
        _added.UnreadMessagesEmail.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_ChangeTheStoredChoice_When_TheAccountTurnsItBackOn()
    {
        // Arrange
        var stored = NotificationPreference.Create(UserId, unreadMessagesEmail: false, ConversationBuilder.DefaultNow);
        GivenStored(stored);

        // Act
        var result = await Update.HandleAsync(new UpdateNotificationSettingsCommand(UserId, true), CancellationToken.None);

        // Assert
        result.Value!.UnreadMessagesEmail.Should().BeTrue();
        stored.UnreadMessagesEmail.Should().BeTrue();
        _repository.Verify(r => r.Add(It.IsAny<NotificationPreference>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_NotSave_When_TheChoiceIsTheSame()
    {
        // Arrange
        GivenStored(NotificationPreference.Create(UserId, unreadMessagesEmail: false, ConversationBuilder.DefaultNow));

        // Act
        var result = await Update.HandleAsync(new UpdateNotificationSettingsCommand(UserId, false), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnTheSaveError_When_SavingFails()
    {
        // Arrange
        GivenStored(null);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ConversationsErrors.Conflict));

        // Act
        var result = await Update.HandleAsync(new UpdateNotificationSettingsCommand(UserId, false), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.Conflict);
    }

    [Fact]
    public async Task Update_Should_ReturnRequired_When_TheValidatorWasSkipped()
    {
        // Act
        var result = await Update.HandleAsync(new UpdateNotificationSettingsCommand(UserId, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.UnreadMessagesEmailRequired);
    }

    [Fact]
    public void Validator_Should_RequireTheChoice_When_ItIsMissing()
    {
        // Act
        var result = new UpdateNotificationSettingsCommandValidator().Validate(new UpdateNotificationSettingsCommand(UserId, null));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ConversationsErrors.UnreadMessagesEmailRequired.Message);
    }

    private void GivenStored(NotificationPreference? preference) =>
        _repository.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(preference);
}
