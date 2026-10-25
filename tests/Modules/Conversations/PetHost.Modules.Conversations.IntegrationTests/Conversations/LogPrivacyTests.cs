using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using static PetHost.Modules.Conversations.IntegrationTests.ConversationsScenario;

namespace PetHost.Modules.Conversations.IntegrationTests.Conversations;

/// <summary>
/// Os logs depois de um fluxo completo (cadastro, login com senha errada e certa, conversa e
/// mensagem), nos níveis configurados da API, não podem ter senha, token, CPF nem o texto da
/// mensagem. Vale para tudo que a API e as bibliotecas escrevem, inclusive o log de requisições.
/// Abaixo desses níveis (Trace do MVC) o ASP.NET Core imprime os argumentos das ações: por isso
/// nenhuma configuração deve baixar <c>Microsoft.AspNetCore</c> para Trace.
/// </summary>
[Collection(ConversationsApiCollection.Name)]
public sealed partial class LogPrivacyTests(ConversationsApiFixture fixture) : IAsyncLifetime
{
    private const string Cpf = "529.982.247-25";
    private const string MessageText = "O portão é 4521, a chave fica embaixo do vaso";

    private readonly CapturingSink _sink = new();
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    private ConversationsScenario _scenario = null!;
    private ILogger _previousStaticLogger = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();

        // Os níveis vêm da configuração da própria API (seção Serilog do appsettings), como em uso;
        // só o destino muda. O log de requisições escreve no logger estático, que o AddSerilog troca.
        _previousStaticLogger = Log.Logger;
        _api = fixture.WithSettings(new Dictionary<string, string>())
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSerilog((provider, logger) => logger
                .ReadFrom.Configuration(provider.GetRequiredService<IConfiguration>())
                .WriteTo.Sink(_sink))));
        _client = _api.CreateClient();
        _scenario = new ConversationsScenario(fixture, _client);
    }

    public async ValueTask DisposeAsync()
    {
        Log.Logger = _previousStaticLogger;
        _client.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Logs_Should_NotContainPasswordsTokensCpfOrMessageText_When_AFullFlowRuns()
    {
        // Arrange + Act: o fluxo inteiro passa pela API (a Camila do cenário é cadastrada com este CPF).
        var pair = await _scenario.ArrangeAsync();

        var wrongLogin = await LoginAsync("Senha@Errada999");
        var login = await LoginAsync(Password);
        var session = Data(await ReadAsync(login));
        var accessToken = session.GetProperty("accessToken").GetString()!;
        var refreshToken = session.GetProperty("refreshToken").GetString()!;

        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(accessToken, conversationId, MessageText);
        await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}/messages", pair.HostToken);

        // Assert
        wrongLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var logs = _sink.Text;
        logs.Should().NotBeEmpty("o fluxo gera logs, e é neles que procuramos");

        logs.Should().NotContain(Password).And.NotContain("Senha@Errada999");
        logs.Should().NotContain(pair.OwnerToken).And.NotContain(pair.HostToken).And.NotContain(accessToken).And.NotContain(refreshToken);
        logs.Should().NotContain(Cpf).And.NotContain(Digits().Replace(Cpf, string.Empty));
        logs.Should().NotContain(MessageText).And.NotContain("4521");
    }

    private Task<HttpResponseMessage> LoginAsync(string password) =>
        _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = "camila@exemplo.com", password, role = "owner" },
            Ct);

    [GeneratedRegex(@"\D")]
    private static partial Regex Digits();

    /// <summary>Guarda cada evento como texto: a mensagem, as propriedades e a exceção.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<string> _events = new();

        public string Text => string.Join('\n', _events);

        public void Emit(LogEvent logEvent)
        {
            var properties = string.Join(' ', logEvent.Properties.Select(p => $"{p.Key}={p.Value}"));
            _events.Enqueue($"{logEvent.RenderMessage(CultureInfo.InvariantCulture)} {properties} {logEvent.Exception}");
        }
    }
}
