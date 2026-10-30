using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Messages.NotifyUnreadMessages;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Preferences;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Messages.NotifyUnreadMessages;

public sealed class NotifyUnreadMessagesCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddHours(4);
    private static readonly NotifyUnreadMessagesCommand Command = new(DelayMinutes: 15, CooldownMinutes: 60, BatchSize: 200);

    private static readonly Guid OtherHostId = TestIds.Of(501);
    private static readonly PartyContact Camila = new(ConversationBuilder.OwnerId, ConversationBuilder.OwnerUserId, "Camila Souza", "camila@exemplo.com", CanBeNotified: true);

    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IUnreadMessagesNotifier> _notifier = new(MockBehavior.Strict);
    private readonly Mock<INotificationPreferenceRepository> _preferences = new(MockBehavior.Strict);
    private readonly NotifyUnreadMessagesCommandHandler _sut;
    private readonly List<UnreadDigest> _sent = [];

    public NotifyUnreadMessagesCommandHandlerTests()
    {
        _sut = new NotifyUnreadMessagesCommandHandler(_queries.Object, _repository.Object, _parties.Object, _preferences.Object, _notifier.Object, new FakeTimeProvider(Now));

        _preferences
            .Setup(p => p.ListUnreadEmailOptOutsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        _notifier.Setup(n => n.SendAsync(It.IsAny<UnreadDigest>(), It.IsAny<CancellationToken>())).Callback<UnreadDigest, CancellationToken>((d, _) => _sent.Add(d)).Returns(Task.CompletedTask);
        _repository.Setup(r => r.MarkUnreadNotifiedAsync(It.IsAny<ConversationId>(), It.IsAny<ConversationParty>(), Now, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, PartyInfo>
        {
            [ConversationBuilder.HostId] = ConversationsTestData.Host,
            [OtherHostId] = new PartyInfo(OtherHostId, TestIds.Of(502), "Hotelzinho Pet Feliz", null),
        });
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
    }

    [Fact]
    public async Task HandleAsync_Should_SendOneDigestPerPersonWithEveryConversation_When_ThereAreUnreadMessages()
    {
        // Arrange: a Camila tem mensagens esperando em duas conversas.
        var withCida = new ConversationId(TestIds.Of(1));
        var withHotel = new ConversationId(TestIds.Of(2));
        GivenRows(
            new UnreadNotificationRow(withCida, ConversationParty.Owner, ConversationBuilder.OwnerId, ConversationBuilder.HostId, 3),
            new UnreadNotificationRow(withHotel, ConversationParty.Owner, ConversationBuilder.OwnerId, OtherHostId, 1));
        GivenOwnerContacts(Camila);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(1);
        var digest = _sent.Single();
        digest.Email.Should().Be("camila@exemplo.com");
        digest.Name.Should().Be("Camila Souza");
        digest.Items.Should().BeEquivalentTo([new UnreadDigestItem("Dona Cida", 3), new UnreadDigestItem("Hotelzinho Pet Feliz", 1)]);
        _repository.Verify(r => r.MarkUnreadNotifiedAsync(withCida, ConversationParty.Owner, Now, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.MarkUnreadNotifiedAsync(withHotel, ConversationParty.Owner, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_AskForMessagesOlderThanTheDelayAndNotificationsOlderThanTheCooldown_When_Run()
    {
        // Arrange
        GivenRows();

        // Act
        await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        _queries.Verify(q => q.ListUnreadToNotifyAsync(Now.AddMinutes(-15), Now.AddMinutes(-60), 200, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SendNothingAndLookUpNobody_When_NothingIsWaiting()
    {
        // Arrange
        GivenRows();

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(0);
        _parties.Verify(p => p.GetContactsAsync(It.IsAny<ConversationParty>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotEmailButStillMark_When_TheRecipientIsSuspended()
    {
        // Arrange
        var conversation = new ConversationId(TestIds.Of(1));
        GivenRows(new UnreadNotificationRow(conversation, ConversationParty.Owner, ConversationBuilder.OwnerId, ConversationBuilder.HostId, 2));
        GivenOwnerContacts(Camila with { CanBeNotified = false });

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(0);
        _sent.Should().BeEmpty();
        _repository.Verify(r => r.MarkUnreadNotifiedAsync(conversation, ConversationParty.Owner, Now, It.IsAny<CancellationToken>()), Times.Once, "senão a pendência voltaria a cada rodada");
    }

    [Fact]
    public async Task HandleAsync_Should_NotEmailButStillMark_When_TheRecipientTurnedTheEmailOff()
    {
        // Arrange
        var conversation = new ConversationId(TestIds.Of(1));
        GivenRows(new UnreadNotificationRow(conversation, ConversationParty.Owner, ConversationBuilder.OwnerId, ConversationBuilder.HostId, 2));
        GivenOwnerContacts(Camila);
        _preferences
            .Setup(p => p.ListUnreadEmailOptOutsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(ConversationBuilder.OwnerUserId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { ConversationBuilder.OwnerUserId });

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(0);
        _sent.Should().BeEmpty();
        _repository.Verify(r => r.MarkUnreadNotifiedAsync(conversation, ConversationParty.Owner, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotEmail_When_TheRecipientAccountIsGone()
    {
        // Arrange
        GivenRows(new UnreadNotificationRow(new ConversationId(TestIds.Of(1)), ConversationParty.Owner, ConversationBuilder.OwnerId, ConversationBuilder.HostId, 2));
        GivenOwnerContacts();

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_Should_EmailTheHostWithTheOwnerName_When_TheHostHasNotRead()
    {
        // Arrange
        var conversation = new ConversationId(TestIds.Of(1));
        GivenRows(new UnreadNotificationRow(conversation, ConversationParty.Host, ConversationBuilder.HostId, ConversationBuilder.OwnerId, 1));
        _parties
            .Setup(p => p.GetContactsAsync(ConversationParty.Host, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PartyContact> { [ConversationBuilder.HostId] = new(ConversationBuilder.HostId, ConversationBuilder.HostUserId, "Aparecida Lima", "cida@exemplo.com", true) });

        // Act
        await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        var digest = _sent.Single();
        digest.Email.Should().Be("cida@exemplo.com");
        digest.Items.Should().ContainSingle().Which.Should().Be(new UnreadDigestItem("Camila Souza", 1));
        _repository.Verify(r => r.MarkUnreadNotifiedAsync(conversation, ConversationParty.Host, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotMark_When_TheEmailCouldNotBeQueued()
    {
        // Arrange
        GivenRows(new UnreadNotificationRow(new ConversationId(TestIds.Of(1)), ConversationParty.Owner, ConversationBuilder.OwnerId, ConversationBuilder.HostId, 2));
        GivenOwnerContacts(Camila);
        _notifier.Setup(n => n.SendAsync(It.IsAny<UnreadDigest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("outbox fora"));

        // Act
        var run = () => _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        await run.Should().ThrowAsync<InvalidOperationException>();
        _repository.Verify(r => r.MarkUnreadNotifiedAsync(It.IsAny<ConversationId>(), It.IsAny<ConversationParty>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never, "o e-mail fica para a próxima rodada");
    }

    private void GivenRows(params UnreadNotificationRow[] rows) =>
        _queries
            .Setup(q => q.ListUnreadToNotifyAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    private void GivenOwnerContacts(params PartyContact[] contacts) =>
        _parties
            .Setup(p => p.GetContactsAsync(ConversationParty.Owner, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contacts.ToDictionary(c => c.ProfileId));
}
