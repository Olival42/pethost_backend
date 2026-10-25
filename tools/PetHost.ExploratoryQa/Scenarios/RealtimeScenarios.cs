using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace PetHost.ExploratoryQa.Scenarios;

/// <summary>
/// Chat entre a tutora Ana e a anfitriã Carla: a mensagem enviada pela API chega pelo hub
/// <c>/hubs/conversations</c> (WebSocket), o texto volta igual, entrada inválida é recusada e
/// quem não é da conversa não a vê.
/// </summary>
internal static class RealtimeScenarios
{
    private const string Area = "Chat e tempo real";
    private const string HubPath = "/hubs/conversations";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    public static async Task RunAsync(QaContext context)
    {
        var conversationId = await StartConversationAsync(context);
        if (conversationId is not { } id)
            return;

        await PushAsync(context, id);
        await InvalidMessagesAsync(context, id);
        await OutsiderAsync(context, id);
        await HubAccessAsync(context);
    }

    private static async Task<Guid?> StartConversationAsync(QaContext context)
    {
        var listings = await context.Api.GetAsync("/api/v1/listings/me", context.Carla.AccessToken);
        var listingId = listings.Data.GetProperty("listings").EnumerateArray().First().GetProperty("id").GetGuid();

        var started = await context.Api.PostAsync("/api/v1/conversations", new { listingId }, context.Ana.AccessToken);
        var passed = started.StatusCode is 200 or 201;
        context.Run.Record("R-01", Area, "Tutora puxa conversa pelo cantinho da anfitriã",
            "201 na primeira vez, 200 com a mesma conversa depois", started.Describe(), passed);

        return passed ? started.Data.GetProperty("id").GetGuid() : null;
    }

    /// <summary>A anfitriã conectada no hub recebe a mensagem e o contador novo.</summary>
    private static async Task PushAsync(QaContext context, Guid conversationId)
    {
        var run = context.Run;
        var marker = "qa-" + Guid.NewGuid().ToString("N");
        var body = $"Olá, Carla! A Pipoca é tímida 🐶 — ação, coração. {marker}";
        context.AddSecret("texto de mensagem do chat", marker);

        await using var host = Connect(context, context.Carla.AccessToken);
        var message = Next(host, "messageCreated");
        var unread = Next(host, "unreadCountChanged");
        await host.StartAsync();

        var watch = Stopwatch.StartNew();
        var sent = await context.Api.PostAsync($"/api/v1/conversations/{conversationId}/messages", new { body }, context.Ana.AccessToken);
        run.Expect("R-02", Area, "Tutora envia mensagem de texto", sent, HttpStatusCode.Created);

        var pushed = await WaitAsync(message);
        watch.Stop();
        run.Record("R-03", Area, "A anfitriã conectada no hub (WebSocket) recebe `messageCreated`", "evento em até 10 s",
            pushed is null ? "nenhum evento em 10 s" : string.Create(CultureInfo.InvariantCulture, $"recebido em {watch.Elapsed.TotalMilliseconds:0} ms após o envio"),
            pushed is not null);

        var received = pushed?.GetProperty("body").GetString();
        run.Record("R-04", Area, "Acentos, travessão e emoji chegam iguais pelo hub", "texto idêntico ao enviado",
            received == body ? "idêntico" : $"diferente: \"{received}\"", received == body);

        var count = await WaitAsync(unread);
        run.Record("R-05", Area, "A anfitriã recebe `unreadCountChanged` com o contador novo", "evento com `total` ≥ 1",
            count is null ? "nenhum evento em 10 s" : $"total = {count.Value.GetProperty("total").GetInt32()}",
            count is not null && count.Value.GetProperty("total").GetInt32() >= 1);

        var history = await context.Api.GetAsync($"/api/v1/conversations/{conversationId}/messages?pageSize=5", context.Carla.AccessToken);
        var stored = history.Status == HttpStatusCode.OK
            && history.Data.GetProperty("items").EnumerateArray().Any(m => m.TryGetProperty("body", out var b) && b.GetString() == body);
        run.Record("R-06", Area, "A mensagem fica no histórico da conversa", "presente em `GET .../messages`",
            history.Describe() + (stored ? ", presente" : ", ausente"), stored);
    }

    private static async Task InvalidMessagesAsync(QaContext context, Guid conversationId)
    {
        var path = $"/api/v1/conversations/{conversationId}/messages";
        var token = context.Ana.AccessToken;

        var blank = await context.Api.PostAsync(path, new { body = "   " }, token);
        context.Run.Expect("R-07", Area, "Mensagem só com espaços", blank, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var tooLong = await context.Api.PostAsync(path, new { body = new string('a', 2001) }, token);
        context.Run.Expect("R-08", Area, "Mensagem com 2.001 caracteres (máximo 2.000)", tooLong, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var control = await context.Api.PostAsync(path, new { body = "antes\u0000depois" }, token);
        context.Run.Expect("R-09", Area, "Mensagem com caractere nulo (`\\u0000`)", control, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>Bruno não é da conversa entre Ana e Carla.</summary>
    private static async Task OutsiderAsync(QaContext context, Guid conversationId)
    {
        var read = await context.Api.GetAsync($"/api/v1/conversations/{conversationId}/messages", context.Bruno.AccessToken);
        context.Run.Expect("R-10", Area, "Outro tutor lê as mensagens da conversa", read, HttpStatusCode.NotFound, "CONVERSATION_NOT_FOUND");

        var write = await context.Api.PostAsync($"/api/v1/conversations/{conversationId}/messages",
            new { body = "Mensagem de quem não é da conversa" }, context.Bruno.AccessToken);
        context.Run.Expect("R-11", Area, "Outro tutor envia mensagem na conversa", write, HttpStatusCode.NotFound, "CONVERSATION_NOT_FOUND");
    }

    /// <summary>A negociação do hub recusa quem não tem token válido de tutor ou anfitrião.</summary>
    private static async Task HubAccessAsync(QaContext context)
    {
        var negotiate = HubPath + "/negotiate?negotiateVersion=1";

        var anonymous = await context.Api.SendAsync(HttpMethod.Post, negotiate);
        context.Run.Record("R-12", Area, "Hub sem token", "401", anonymous.Describe(), anonymous.Status == HttpStatusCode.Unauthorized);

        var tampered = await context.Api.SendAsync(HttpMethod.Post,
            negotiate + "&access_token=" + Uri.EscapeDataString(Jwt.WithClaim(context.Ana.AccessToken, "sub", Guid.NewGuid().ToString())));
        context.Run.Record("R-13", Area, "Hub com token adulterado", "401", tampered.Describe(), tampered.Status == HttpStatusCode.Unauthorized);

        if (context.Admin is null)
        {
            context.Run.Skip("R-14", Area, "Hub com token de admin", "sem as credenciais do admin");
            return;
        }

        var admin = await context.Api.SendAsync(HttpMethod.Post, negotiate + "&access_token=" + Uri.EscapeDataString(context.Admin.AccessToken));
        context.Run.Record("R-14", Area, "Hub com token de admin (o chat é só de tutor e anfitrião)", "403", admin.Describe(),
            admin.Status == HttpStatusCode.Forbidden);
    }

    private static HubConnection Connect(QaContext context, string token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(context.Api.BaseAddress, HubPath), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

    private static Task<JsonElement> Next(HubConnection connection, string eventName)
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>(eventName, payload => received.TrySetResult(payload));

        return received.Task;
    }

    private static async Task<JsonElement?> WaitAsync(Task<JsonElement> pending)
    {
        try
        {
            return await pending.WaitAsync(Wait);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
