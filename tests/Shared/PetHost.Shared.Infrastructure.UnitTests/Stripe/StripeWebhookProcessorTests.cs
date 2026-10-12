using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PetHost.Shared.Infrastructure.Stripe;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Stripe;

/// <summary>Webhook do Stripe: só entra evento assinado, e cada um vai para quem trata o tipo.</summary>
public sealed class StripeWebhookProcessorTests
{
    private const string AccountId = "acct_1Webhook";

    private readonly Mock<IStripeWebhookHandler> _accountHandler = new();
    private readonly Mock<IStripeWebhookHandler> _otherHandler = new();

    public StripeWebhookProcessorTests()
    {
        _accountHandler.Setup(h => h.CanHandle("account.updated")).Returns(true);
        _otherHandler.Setup(h => h.CanHandle(It.IsAny<string>())).Returns(false);
    }

    private StripeWebhookProcessor Processor(string? webhookSecret = StripeWebhookSigner.Secret, string? connectSecret = null) =>
        new(
            Options.Create(new StripeOptions { WebhookSecret = webhookSecret ?? string.Empty, ConnectWebhookSecret = connectSecret }),
            [_accountHandler.Object, _otherHandler.Object],
            NullLogger<StripeWebhookProcessor>.Instance);

    [Fact]
    public async Task ProcessAsync_Should_DispatchToTheHandler_When_SignatureIsValid()
    {
        // Arrange
        var payload = StripeWebhookSigner.AccountUpdated(AccountId, "evt_1");
        StripeWebhookEvent? received = null;
        _accountHandler
            .Setup(h => h.HandleAsync(It.IsAny<StripeWebhookEvent>(), It.IsAny<CancellationToken>()))
            .Callback<StripeWebhookEvent, CancellationToken>((e, _) => received = e)
            .Returns(Task.CompletedTask);

        // Act
        var result = await Processor().ProcessAsync(payload, StripeWebhookSigner.Sign(payload), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        received.Should().NotBeNull();
        received!.Id.Should().Be("evt_1");
        received.Type.Should().Be("account.updated");
        received.ConnectedAccountId.Should().Be(AccountId);
        received.ObjectId.Should().Be(AccountId);
        received.LiveMode.Should().BeFalse();
        _otherHandler.Verify(h => h.HandleAsync(It.IsAny<StripeWebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_Should_AcceptTheConnectSecret_When_ItIsADifferentDestination()
    {
        // Arrange
        const string connectSecret = "whsec_connect_destination";
        var payload = StripeWebhookSigner.AccountUpdated(AccountId);

        // Act
        var result = await Processor(connectSecret: connectSecret)
            .ProcessAsync(payload, StripeWebhookSigner.Sign(payload, connectSecret), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _accountHandler.Verify(h => h.HandleAsync(It.IsAny<StripeWebhookEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("t=1,v1=deadbeef")]
    public async Task ProcessAsync_Should_RejectWithoutDispatching_When_SignatureIsMissingOrWrong(string? signature)
    {
        // Arrange
        var payload = StripeWebhookSigner.AccountUpdated(AccountId);

        // Act
        var result = await Processor().ProcessAsync(payload, signature, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(StripeWebhookErrors.SignatureInvalid);
        _accountHandler.Verify(h => h.HandleAsync(It.IsAny<StripeWebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_Should_Reject_When_SignedWithAnotherSecret()
    {
        // Arrange
        var payload = StripeWebhookSigner.AccountUpdated(AccountId);

        // Act
        var result = await Processor().ProcessAsync(payload, StripeWebhookSigner.Sign(payload, "whsec_de_outra_pessoa"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(StripeWebhookErrors.SignatureInvalid);
    }

    [Fact]
    public async Task ProcessAsync_Should_Reject_When_BodyWasChangedAfterSigning()
    {
        // Arrange
        var payload = StripeWebhookSigner.AccountUpdated(AccountId);
        var signature = StripeWebhookSigner.Sign(payload);

        // Act
        var result = await Processor().ProcessAsync(payload.Replace(AccountId, "acct_1Forjada", StringComparison.Ordinal), signature, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(StripeWebhookErrors.SignatureInvalid);
    }

    [Fact]
    public async Task ProcessAsync_Should_Reject_When_SignatureIsOlderThanTheTolerance()
    {
        // Arrange
        var payload = StripeWebhookSigner.AccountUpdated(AccountId);
        var old = DateTimeOffset.UtcNow.AddSeconds(-(StripeWebhookProcessor.ToleranceSeconds + 60));

        // Act
        var result = await Processor().ProcessAsync(payload, StripeWebhookSigner.Sign(payload, signedAt: old), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(StripeWebhookErrors.SignatureInvalid, "evento capturado e reenviado depois não entra");
    }

    [Fact]
    public async Task ProcessAsync_Should_ReturnUnavailable_When_NoSecretIsConfigured()
    {
        // Arrange
        var payload = StripeWebhookSigner.AccountUpdated(AccountId);

        // Act
        var result = await Processor(webhookSecret: null).ProcessAsync(payload, StripeWebhookSigner.Sign(payload), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(StripeWebhookErrors.NotConfigured);
    }

    [Fact]
    public async Task ProcessAsync_Should_AcceptAndIgnore_When_NoHandlerTakesTheType()
    {
        // Arrange
        var payload = StripeWebhookSigner.AccountUpdated(AccountId).Replace("account.updated", "payout.paid", StringComparison.Ordinal);

        // Act
        var result = await Processor().ProcessAsync(payload, StripeWebhookSigner.Sign(payload), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("o Stripe não deve reenviar um evento que só não interessa");
        _accountHandler.Verify(h => h.HandleAsync(It.IsAny<StripeWebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_Should_LetTheExceptionThrough_When_TheHandlerFails()
    {
        // Arrange
        var payload = StripeWebhookSigner.AccountUpdated(AccountId);
        _accountHandler
            .Setup(h => h.HandleAsync(It.IsAny<StripeWebhookEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stripe fora do ar"));

        // Act
        var act = () => Processor().ProcessAsync(payload, StripeWebhookSigner.Sign(payload), CancellationToken.None);

        // Assert: vira 500 e o Stripe tenta de novo mais tarde.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void WebhookSecrets_Should_IgnoreBlankAndRepeatedSecrets_When_Listed()
    {
        // Arrange
        var options = new StripeOptions { WebhookSecret = " whsec_a ", ConnectWebhookSecret = "whsec_a" };

        // Act / Assert
        options.WebhookSecrets.Should().Equal("whsec_a");
        new StripeOptions().WebhookSecrets.Should().BeEmpty();
        new StripeOptions().IsConfigured.Should().BeFalse();
    }
}
