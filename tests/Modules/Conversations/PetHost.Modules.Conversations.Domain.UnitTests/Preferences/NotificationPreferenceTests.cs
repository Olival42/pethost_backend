using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Preferences;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Preferences;

public sealed class NotificationPreferenceTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow;

    [Fact]
    public void Create_Should_KeepTheAccountAndTheChoice_When_Valid()
    {
        // Act
        var preference = NotificationPreference.Create(ConversationBuilder.HostUserId, unreadMessagesEmail: false, Now);

        // Assert
        preference.Id.Should().Be(ConversationBuilder.HostUserId);
        preference.UnreadMessagesEmail.Should().BeFalse();
        preference.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_Should_Throw_When_TheUserIdIsEmpty()
    {
        // Act
        var create = () => NotificationPreference.Create(Guid.Empty, unreadMessagesEmail: true, Now);

        // Assert
        create.Should().Throw<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public void SetUnreadMessagesEmail_Should_ChangeTheChoice_When_ItIsDifferent()
    {
        // Arrange
        var preference = NotificationPreference.Create(ConversationBuilder.HostUserId, unreadMessagesEmail: false, Now);

        // Act
        var changed = preference.SetUnreadMessagesEmail(true, Now.AddDays(1));

        // Assert
        changed.Should().BeTrue();
        preference.UnreadMessagesEmail.Should().BeTrue();
        preference.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void SetUnreadMessagesEmail_Should_ChangeNothing_When_ItIsTheSame()
    {
        // Arrange
        var preference = NotificationPreference.Create(ConversationBuilder.HostUserId, unreadMessagesEmail: false, Now);

        // Act
        var changed = preference.SetUnreadMessagesEmail(false, Now.AddDays(1));

        // Assert
        changed.Should().BeFalse();
        preference.UpdatedAt.Should().Be(Now);
    }
}
