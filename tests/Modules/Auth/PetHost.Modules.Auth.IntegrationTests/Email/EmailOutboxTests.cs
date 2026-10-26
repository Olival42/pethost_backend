using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Shared.Infrastructure.Email;
using PetHost.Shared.Infrastructure.Email.Persistence;
using Xunit;

namespace PetHost.Modules.Auth.IntegrationTests.Email;

/// <summary>A caixa de saída persistente (<c>email.outbox_emails</c>) com o despachante de verdade.</summary>
[Collection(AuthApiCollection.Name)]
public sealed class EmailOutboxTests(AuthApiFixture fixture) : IAsyncLifetime
{
    private const string To = "fila@exemplo.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync()
    {
        fixture.Emails.Clear();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Enqueue_Should_DeliverTheEmailAndRemoveItFromTheTable()
    {
        await fixture.Services.GetRequiredService<IEmailOutbox>().EnqueueAsync(Message("Olá"), Ct);

        var delivered = (await fixture.Emails.WaitForAsync(To, 1, Ct)).Should().ContainSingle().Which;
        delivered.Subject.Should().Be("Olá");
        delivered.HtmlBody.Should().Contain("<!doctype html>").And.Contain("html</p>").And.Contain("#F26B3A");
        delivered.TextBody.Should().Contain("texto").And.Contain("equipe PetHost");
        delivered.HtmlBody.Should().Contain("href=\"https://app.pethost.test/\"");
        delivered.TextBody.Should().Contain("https://app.pethost.test/");

        await WaitUntilAsync(async () => await CountAsync() == 0);
        (await CountAsync()).Should().Be(0, "o corpo pode ter token: e-mail entregue não fica no banco");
    }

    [Fact]
    public async Task Dispatcher_Should_DeliverEmailsLeftInTheTable_When_TheApiRestarts()
    {
        // Simula um e-mail gravado por uma subida anterior que caiu antes de enviar:
        // ninguém avisa o despachante, ele acha o e-mail na varredura.
        await using (var context = await NewContextAsync())
        {
            context.Outbox.Add(OutboxEmail.From(Message("Ficou na fila"), Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddMinutes(-1)));
            await context.SaveChangesAsync(Ct);
        }

        (await fixture.Emails.WaitForAsync(To, 1, Ct, TimeSpan.FromSeconds(20))).Should().ContainSingle()
            .Which.Subject.Should().Be("Ficou na fila");
    }

    [Fact]
    public async Task Dispatcher_Should_KeepTheEmailAndScheduleARetry_When_SmtpFails()
    {
        fixture.Emails.Failure = new InvalidOperationException("SMTP fora do ar");

        await fixture.Services.GetRequiredService<IEmailOutbox>().EnqueueAsync(Message("Importante"), Ct);

        await WaitUntilAsync(async () => (await PendingAsync())?.LastError is not null);
        var pending = await PendingAsync();

        pending.Should().NotBeNull("e-mail que falhou continua na fila");
        pending!.Attempts.Should().Be(1);
        pending.LastError.Should().Contain("SMTP fora do ar");
        pending.FailedAt.Should().BeNull();
        pending.NextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(20), "a próxima tentativa espera ~30 s");
        fixture.Emails.Sent.Should().BeEmpty();
    }

    private static EmailMessage Message(string subject) =>
        new(To, "Fila", subject, "texto", "<p>html</p>");

    private Task<EmailDbContext> NewContextAsync() =>
        fixture.Services.GetRequiredService<IDbContextFactory<EmailDbContext>>().CreateDbContextAsync(Ct);

    private async Task<int> CountAsync()
    {
        await using var context = await NewContextAsync();
        return await context.Outbox.CountAsync(Ct);
    }

    private async Task<OutboxEmail?> PendingAsync()
    {
        await using var context = await NewContextAsync();
        return await context.Outbox.AsNoTracking().SingleOrDefaultAsync(e => e.ToAddress == To, Ct);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!await condition() && DateTime.UtcNow < deadline)
            await Task.Delay(25, Ct);
    }
}
