using FluentAssertions;
using Microsoft.Extensions.Options;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Infrastructure.Notifications;
using PetHost.Modules.Auth.Infrastructure.Security;
using PetHost.Shared.Infrastructure.Email;
using Xunit;

namespace PetHost.Modules.Auth.Infrastructure.UnitTests.Notifications;

public sealed class PasswordResetEmailNotifierTests
{
    [Theory]
    [InlineData(null, "https://app.example.test", "https://app.example.test/reset-password#token=a%2Bb%26c")]
    [InlineData("https://custom.example.test/reset#token={token}", "https://app.example.test", "https://custom.example.test/reset#token=a%2Bb%26c")]
    [InlineData(null, null, null)]
    public async Task Notify_Should_UseTheConfiguredFrontendAndPreserveTheManualCode(string? resetUrl, string? appUrl, string? expected)
    {
        var outbox = new CapturingOutbox();
        var notifier = new PasswordResetEmailNotifier(outbox,
            Options.Create(new PasswordResetOptions { ResetUrl = resetUrl }),
            Options.Create(new EmailOptions { AppUrl = appUrl }));

        await notifier.NotifyAsync(new PasswordResetNotification("test@example.invalid", "Ana <Teste>", "a+b&c", DateTimeOffset.UtcNow.AddMinutes(30)), TestContext.Current.CancellationToken);

        var queued = outbox.Message;
        queued.Should().NotBeNull();
        queued!.TextBody.Should().Contain("30 minutos").And.Contain("sua senha continua a mesma");
        queued.HtmlBody.Should().Contain("Ana &lt;Teste&gt;").And.NotContain("Ana <Teste>");
        if (expected is null)
        {
            queued.TextBody.Should().Contain("a+b&c");
            queued.HtmlBody.Should().Contain("a+b&amp;c");
        }
        else
        {
            queued.TextBody.Should().Contain(expected);
            queued.HtmlBody.Should().Contain("Você não precisa entrar na conta antes").And.NotContain("<strong>a+b&amp;c</strong>");
        }
        (queued.Action?.Url).Should().Be(expected);
    }

    private sealed class CapturingOutbox : IEmailOutbox
    {
        public EmailMessage? Message { get; private set; }

        public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Message = message;
            return ValueTask.CompletedTask;
        }
    }
}
