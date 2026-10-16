using System.Net;
using FluentAssertions;
using Xunit;
using static PetHost.Modules.Conversations.IntegrationTests.ConversationsScenario;

namespace PetHost.Modules.Conversations.IntegrationTests.Conversations;

/// <summary>
/// Moderação de ponta a ponta: bloqueio pelos dois lados (ninguém envia, os dois leem, só quem
/// bloqueou desbloqueia), denúncia de mensagem recebida e a fila do admin (listar, abrir com o
/// contexto, analisar).
/// </summary>
[Collection(ConversationsApiCollection.Name)]
public sealed class ConversationModerationTests(ConversationsApiFixture fixture) : IAsyncLifetime
{
    private const string ReportsRoute = $"{ConversationsRoute}/reports";

    private HttpClient _client = null!;
    private ConversationsScenario _scenario = null!;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        _client = fixture.CreateClient();
        _scenario = new ConversationsScenario(fixture, _client);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Block_Should_StopBothSidesFromSendingButLetThemRead_When_TheHostBlocks()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!");
        var messages = $"{ConversationsRoute}/{conversationId}/messages";

        // Act
        var blocked = await _scenario.SendAsync(HttpMethod.Put, $"{ConversationsRoute}/{conversationId}/block", pair.HostToken);

        // Assert
        blocked.StatusCode.Should().Be(HttpStatusCode.OK);
        var asHost = Data(await ReadAsync(blocked));
        asHost.GetProperty("blocked").GetBoolean().Should().BeTrue();
        asHost.GetProperty("blockedByMe").GetBoolean().Should().BeTrue();

        var asOwner = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}", pair.OwnerToken)));
        asOwner.GetProperty("blocked").GetBoolean().Should().BeTrue();
        asOwner.GetProperty("blockedByMe").GetBoolean().Should().BeFalse();

        var ownerSends = await _scenario.SendAsync(HttpMethod.Post, messages, pair.OwnerToken, new { body = "Por que?" });
        ownerSends.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(ownerSends)).Should().Be("CONVERSATION_BLOCKED");

        var hostSends = await _scenario.SendAsync(HttpMethod.Post, messages, pair.HostToken, new { body = "Tchau" });
        Code(await ReadAsync(hostSends)).Should().Be("CONVERSATION_BLOCKED", "o bloqueio trava os dois lados");

        var hostPhoto = await _scenario.SendPhotoAsync(pair.HostToken, conversationId, "foto");
        Code(await ReadAsync(hostPhoto)).Should().Be("CONVERSATION_BLOCKED");

        (await _scenario.SendAsync(HttpMethod.Get, messages, pair.OwnerToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _scenario.SendAsync(HttpMethod.Get, messages, pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unblock_Should_BeOnlyForWhoBlocked_When_TheOtherSideTries()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var block = $"{ConversationsRoute}/{conversationId}/block";
        (await _scenario.SendAsync(HttpMethod.Put, block, pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var ownerUnblocks = await _scenario.SendAsync(HttpMethod.Delete, block, pair.OwnerToken);
        var ownerBlocks = await _scenario.SendAsync(HttpMethod.Put, block, pair.OwnerToken);
        var hostBlocksAgain = await _scenario.SendAsync(HttpMethod.Put, block, pair.HostToken);
        var hostUnblocks = await _scenario.SendAsync(HttpMethod.Delete, block, pair.HostToken);

        // Assert
        ownerUnblocks.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(ownerUnblocks)).Should().Be("CONVERSATION_UNBLOCK_FORBIDDEN");
        ownerBlocks.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(ownerBlocks)).Should().Be("CONVERSATION_ALREADY_BLOCKED");
        hostBlocksAgain.StatusCode.Should().Be(HttpStatusCode.OK, "bloquear de novo é idempotente");
        hostUnblocks.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(hostUnblocks)).GetProperty("blocked").GetBoolean().Should().BeFalse();
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Voltou!");
    }

    [Fact]
    public async Task Block_Should_ReturnNotFound_When_TheConversationIsSomeoneElses()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var stranger = await _scenario.RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35", "João Lima");

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Put, $"{ConversationsRoute}/{conversationId}/block", stranger);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Report_Should_EnterTheAdminQueue_When_TheOwnerReportsAMessageFromTheHost()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Quanto fica a diária?");
        var hostMessage = await _scenario.SendTextAsync(pair.HostToken, conversationId, "Me paga no Pix por fora que sai mais barato.");
        var messageId = hostMessage.GetProperty("id").GetGuid();
        var admin = await _scenario.AdminTokenAsync();

        // Act
        var reported = await _scenario.ReportAsync(pair.OwnerToken, conversationId, messageId, "scam", "Pediu pagamento por fora.");

        // Assert
        reported.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = Data(await ReadAsync(reported));
        receipt.GetProperty("status").GetString().Should().Be("pending");
        var reportId = receipt.GetProperty("id").GetGuid();
        reported.Headers.Location!.ToString().Should().Be($"{ReportsRoute}/{reportId}");

        var queue = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, ReportsRoute, admin)));
        queue.GetProperty("totalCount").GetInt64().Should().Be(1);
        var item = queue.GetProperty("items")[0];
        item.GetProperty("reason").GetString().Should().Be("scam");
        item.GetProperty("reportedBy").GetString().Should().Be("owner");
        item.GetProperty("reporter").GetProperty("name").GetString().Should().Be("Camila Souza");
        item.GetProperty("reported").GetProperty("name").GetString().Should().Be("Dona Cida");
        item.GetProperty("message").GetProperty("body").GetString().Should().Be("Me paga no Pix por fora que sai mais barato.");

        var detail = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReportsRoute}/{reportId}", admin)));
        detail.GetProperty("context").GetArrayLength().Should().Be(1);
        detail.GetProperty("context")[0].GetProperty("body").GetString().Should().Be("Quanto fica a diária?");
    }

    [Fact]
    public async Task Report_Should_BeRefused_When_ItIsTheOwnMessageARepeatAnotherConversationOrMissingTheDescription()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var ownMessage = (await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!")).GetProperty("id").GetGuid();
        var hostMessage = (await _scenario.SendTextAsync(pair.HostToken, conversationId, "Spam spam spam")).GetProperty("id").GetGuid();
        (await _scenario.ReportAsync(pair.OwnerToken, conversationId, hostMessage, "spam")).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var own = await _scenario.ReportAsync(pair.OwnerToken, conversationId, ownMessage, "spam");
        var repeat = await _scenario.ReportAsync(pair.OwnerToken, conversationId, hostMessage, "harassment");
        var missing = await _scenario.ReportAsync(pair.OwnerToken, conversationId, Guid.CreateVersion7(), "spam");
        var noDescription = await _scenario.ReportAsync(pair.HostToken, conversationId, ownMessage, "other");

        // Assert
        own.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(own)).Should().Be("CONVERSATION_REPORT_OWN_MESSAGE");
        repeat.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(repeat)).Should().Be("CONVERSATION_REPORT_ALREADY_EXISTS");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(missing)).Should().Be("CONVERSATION_MESSAGE_NOT_FOUND");
        noDescription.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(noDescription)).Should().Contain("description");
    }

    [Fact]
    public async Task Resolve_Should_CloseTheReportAndTakeItOutOfThePendingQueue_When_TheAdminAnalyses()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var hostMessage = (await _scenario.SendTextAsync(pair.HostToken, conversationId, "Você é chato.")).GetProperty("id").GetGuid();
        var reportId = Data(await ReadAsync(await _scenario.ReportAsync(pair.OwnerToken, conversationId, hostMessage, "harassment"))).GetProperty("id").GetGuid();
        var admin = await _scenario.AdminTokenAsync();
        var resolution = $"{ReportsRoute}/{reportId}/resolution";

        // Act
        var resolved = await _scenario.SendAsync(HttpMethod.Put, resolution, admin, new { outcome = "upheld", note = "Ofensa confirmada; anfitriã advertida." });
        var again = await _scenario.SendAsync(HttpMethod.Put, resolution, admin, new { outcome = "dismissed", note = "Mudei de ideia." });

        // Assert
        resolved.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = Data(await ReadAsync(resolved));
        body.GetProperty("status").GetString().Should().Be("upheld");
        body.GetProperty("resolutionNote").GetString().Should().Be("Ofensa confirmada; anfitriã advertida.");
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(again)).Should().Be("CONVERSATION_REPORT_ALREADY_RESOLVED");

        Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, ReportsRoute, admin))).GetProperty("totalCount").GetInt64().Should().Be(0);
        Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReportsRoute}?status=all", admin))).GetProperty("totalCount").GetInt64().Should().Be(1);
        Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReportsRoute}?status=upheld", admin))).GetProperty("totalCount").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Upheld_Should_BlockTheConversationForBothSidesForGood_When_TheAdminUpholdsAReport()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var hostMessage = (await _scenario.SendTextAsync(pair.HostToken, conversationId, "Me paga no Pix por fora.")).GetProperty("id").GetGuid();
        var reportId = Data(await ReadAsync(await _scenario.ReportAsync(pair.OwnerToken, conversationId, hostMessage, "scam"))).GetProperty("id").GetGuid();
        var admin = await _scenario.AdminTokenAsync();
        var messages = $"{ConversationsRoute}/{conversationId}/messages";
        var block = $"{ConversationsRoute}/{conversationId}/block";

        // Act
        (await _scenario.SendAsync(HttpMethod.Put, $"{ReportsRoute}/{reportId}/resolution", admin, new { outcome = "upheld", note = "Golpe confirmado." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert
        var asOwner = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}", pair.OwnerToken)));
        asOwner.GetProperty("blocked").GetBoolean().Should().BeTrue();
        asOwner.GetProperty("blockedByModeration").GetBoolean().Should().BeTrue();
        asOwner.GetProperty("blockedByMe").GetBoolean().Should().BeFalse();

        foreach (var token in new[] { pair.OwnerToken, pair.HostToken })
        {
            var sent = await _scenario.SendAsync(HttpMethod.Post, messages, token, new { body = "Ainda dá?" });
            sent.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            Code(await ReadAsync(sent)).Should().Be("CONVERSATION_BLOCKED_BY_MODERATION");
        }

        // Bloquear e desbloquear pelo lado não tira o bloqueio da moderação.
        (await _scenario.SendAsync(HttpMethod.Put, block, pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var unblocked = await _scenario.SendAsync(HttpMethod.Delete, block, pair.HostToken);
        Data(await ReadAsync(unblocked)).GetProperty("blocked").GetBoolean().Should().BeTrue();
        Code(await ReadAsync(await _scenario.SendAsync(HttpMethod.Post, messages, pair.HostToken, new { body = "E agora?" })))
            .Should().Be("CONVERSATION_BLOCKED_BY_MODERATION");

        (await _scenario.SendAsync(HttpMethod.Get, messages, pair.OwnerToken)).StatusCode.Should().Be(HttpStatusCode.OK, "os dois continuam lendo");
    }

    [Fact]
    public async Task Report_Should_EmailTheAdminWithoutTheMessage_When_ANewReportArrives()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var hostMessage = (await _scenario.SendTextAsync(pair.HostToken, conversationId, "Me paga no Pix por fora.")).GetProperty("id").GetGuid();

        // Act
        var reportId = Data(await ReadAsync(await _scenario.ReportAsync(pair.OwnerToken, conversationId, hostMessage, "scam", "Golpe do Pix.")))
            .GetProperty("id").GetGuid();

        // Assert
        var email = await fixture.WaitForEmailAsync(e => e.ToAddress == ConversationsApiFixture.ModerationAlertEmail, TimeSpan.FromSeconds(15));
        email.Should().NotBeNull("a caixa de saída entrega o aviso ao admin em segundo plano");
        email!.Subject.Should().Be("PetHost — nova denúncia no chat");
        email.TextBody.Should().Contain(reportId.ToString()).And.Contain("golpe").And.Contain("tutor");
        email.TextBody.Should().NotContain("Pix", "o e-mail sai do sistema: nem a mensagem nem a descrição vão nele");
    }

    [Fact]
    public async Task LiftModerationBlock_Should_LetBothSidesSendAgain_When_TheAdminLiftsIt()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var hostMessage = (await _scenario.SendTextAsync(pair.HostToken, conversationId, "Você é chato.")).GetProperty("id").GetGuid();
        var reportId = Data(await ReadAsync(await _scenario.ReportAsync(pair.OwnerToken, conversationId, hostMessage, "harassment"))).GetProperty("id").GetGuid();
        var admin = await _scenario.AdminTokenAsync();
        (await _scenario.SendAsync(HttpMethod.Put, $"{ReportsRoute}/{reportId}/resolution", admin, new { outcome = "upheld", note = "Ofensa." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var lift = $"{ConversationsRoute}/{conversationId}/moderation-block";

        // Act
        var lifted = await _scenario.SendAsync(HttpMethod.Delete, lift, admin);
        var again = await _scenario.SendAsync(HttpMethod.Delete, lift, admin);

        // Assert
        lifted.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(lifted)).GetProperty("blocked").GetBoolean().Should().BeFalse();
        again.StatusCode.Should().Be(HttpStatusCode.OK, "tirar de novo é idempotente");
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Voltamos a conversar.");
        await _scenario.SendTextAsync(pair.HostToken, conversationId, "Desculpe pelo que falei.");
        Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReportsRoute}/{reportId}", admin)))
            .GetProperty("status").GetString().Should().Be("upheld", "a denúncia continua procedente");
    }

    [Fact]
    public async Task LiftModerationBlock_Should_BeOnlyForTheAdmin_When_ASideTries()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var admin = await _scenario.AdminTokenAsync();

        // Act
        var host = await _scenario.SendAsync(HttpMethod.Delete, $"{ConversationsRoute}/{conversationId}/moderation-block", pair.HostToken);
        var missing = await _scenario.SendAsync(HttpMethod.Delete, $"{ConversationsRoute}/{Guid.CreateVersion7()}/moderation-block", admin);

        // Assert
        host.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(missing)).Should().Be("CONVERSATION_NOT_FOUND");
    }

    [Fact]
    public async Task Dismissed_Should_LeaveTheConversationOpen_When_TheAdminDismissesAReport()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var hostMessage = (await _scenario.SendTextAsync(pair.HostToken, conversationId, "Bom dia!")).GetProperty("id").GetGuid();
        var reportId = Data(await ReadAsync(await _scenario.ReportAsync(pair.OwnerToken, conversationId, hostMessage, "spam"))).GetProperty("id").GetGuid();
        var admin = await _scenario.AdminTokenAsync();

        // Act
        (await _scenario.SendAsync(HttpMethod.Put, $"{ReportsRoute}/{reportId}/resolution", admin, new { outcome = "dismissed", note = "Mensagem normal." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Bom dia, Dona Cida!");
    }

    [Fact]
    public async Task AdminRoutes_Should_BeOnlyForTheAdmin_When_AnOwnerOrHostTries()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var admin = await _scenario.AdminTokenAsync();

        // Act
        var owner = await _scenario.SendAsync(HttpMethod.Get, ReportsRoute, pair.OwnerToken);
        var host = await _scenario.SendAsync(HttpMethod.Put, $"{ReportsRoute}/{Guid.CreateVersion7()}/resolution", pair.HostToken, new { outcome = "upheld", note = "x" });
        var anonymous = await _scenario.SendAsync(HttpMethod.Get, ReportsRoute, token: null);
        var badStatus = await _scenario.SendAsync(HttpMethod.Get, $"{ReportsRoute}?status=closed", admin);
        var missing = await _scenario.SendAsync(HttpMethod.Get, $"{ReportsRoute}/{Guid.CreateVersion7()}", admin);

        // Assert
        owner.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        badStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(badStatus)).Should().Contain("status");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(missing)).Should().Be("CONVERSATION_REPORT_NOT_FOUND");
    }
}
