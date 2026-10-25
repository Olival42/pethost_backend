using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PetHost.ExploratoryQa;

/// <summary>
/// Cliente HTTP do roteiro. Guarda toda resposta recebida, para as conferências que olham a
/// execução inteira (nenhuma resposta com stack trace, por exemplo).
/// </summary>
internal sealed class ApiClient(HttpClient http)
{
    private readonly ConcurrentQueue<ApiCall> _calls = new();

    public Uri BaseAddress => http.BaseAddress!;

    public IReadOnlyCollection<ApiCall> Calls => _calls;

    public Task<ApiCall> GetAsync(string path, string? token = null) =>
        SendAsync(HttpMethod.Get, path, token: token);

    public Task<ApiCall> PostAsync(string path, object? body, string? token = null) =>
        SendAsync(HttpMethod.Post, path, body, token);

    public async Task<ApiCall> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? token = null,
        Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is HttpContent content)
            request.Content = content;
        else if (body is not null)
            request.Content = JsonContent.Create(body, options: Json.Options);

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        configure?.Invoke(request);

        var watch = Stopwatch.StartNew();
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        watch.Stop();

        var call = new ApiCall(method.Method, path, response.StatusCode, text, response.Headers, watch.Elapsed);
        _calls.Enqueue(call);

        return call;
    }
}

/// <summary>Uma resposta da API, com o envelope já lido.</summary>
internal sealed class ApiCall
{
    public ApiCall(string method, string path, HttpStatusCode status, string body, HttpResponseHeaders headers, TimeSpan elapsed)
    {
        Method = method;
        Path = path;
        Status = status;
        Body = body;
        Headers = headers;
        Elapsed = elapsed;
        Envelope = Json.TryParse(body);
    }

    public string Method { get; }

    public string Path { get; }

    public HttpStatusCode Status { get; }

    public string Body { get; }

    public HttpResponseHeaders Headers { get; }

    public TimeSpan Elapsed { get; }

    /// <summary>O corpo lido como JSON; nulo quando não é JSON.</summary>
    public JsonElement? Envelope { get; }

    public bool IsEnvelope =>
        Envelope is { ValueKind: JsonValueKind.Object } json
        && json.TryGetProperty("success", out var success)
        && success.ValueKind is JsonValueKind.True or JsonValueKind.False
        && json.TryGetProperty("timestamp", out _);

    public bool Succeeded => IsEnvelope && Envelope!.Value.GetProperty("success").GetBoolean();

    public JsonElement Data => Envelope!.Value.GetProperty("data");

    public string? ErrorCode => Error("code");

    public string? ErrorMessage => Error("message");

    /// <summary>Os campos citados nos detalhes de um erro de validação.</summary>
    public IReadOnlyList<string> ErrorFields
    {
        get
        {
            if (!IsEnvelope
                || !Envelope!.Value.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. details.EnumerateArray()
                .Where(d => d.ValueKind == JsonValueKind.Object && d.TryGetProperty("field", out _))
                .Select(d => d.GetProperty("field").GetString() ?? "")];
        }
    }

    public int StatusCode => (int)Status;

    /// <summary>Resumo para o relatório: status, código de erro e tempo.</summary>
    public string Describe()
    {
        var code = ErrorCode is { } c ? " " + c : "";
        var envelope = IsEnvelope ? "" : " (sem envelope)";

        return string.Create(CultureInfo.InvariantCulture, $"{StatusCode}{code}{envelope}, {Elapsed.TotalMilliseconds:0} ms");
    }

    private string? Error(string property) =>
        IsEnvelope
        && Envelope!.Value.TryGetProperty("error", out var error)
        && error.ValueKind == JsonValueKind.Object
        && error.TryGetProperty(property, out var value)
            ? value.GetString()
            : null;
}

internal static class Json
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    public static JsonElement? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(text, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
