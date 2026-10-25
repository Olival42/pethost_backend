using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PetHost.ExploratoryQa.Scenarios;

/// <summary>
/// Os logs que a API escreveu durante o roteiro (JSON do Serilog, uma linha por evento): cada
/// requisição registrada, o rate limit avisado, nenhum erro inesperado e nenhum dado sensível.
/// Também confere que nenhuma resposta da execução expôs detalhe interno.
/// </summary>
internal static class LogScenarios
{
    private const string Area = "Logs";

    private static readonly string[] InternalDetails =
        ["   at ", "Exception:", "StackTrace", "Npgsql", "Microsoft.EntityFrameworkCore", "System.InvalidOperationException"];

    public static async Task RunAsync(QaContext context)
    {
        CheckResponses(context);

        var (source, lines) = await ReadLogsAsync(context);
        if (lines is null)
        {
            context.Run.Skip("L-01", Area, "Logs da API durante o roteiro", source);
            return;
        }

        var events = lines.Select(Parse).Where(e => e is not null).Select(e => e!.Value).ToList();
        var run = context.Run;

        var requestLogged = events.Any(e =>
            Template(e).StartsWith("HTTP ", StringComparison.Ordinal)
            && Property(e, "RequestPath") == "/api/v1/auth/sessions/login"
            && Property(e, "StatusCode") == "401");
        run.Record("L-01", Area, "Cada requisição vira uma linha com método, rota, status e tempo",
            "linha do `POST /api/v1/auth/sessions/login` com status 401",
            $"{events.Count} eventos lidos; {(requestLogged ? "linha encontrada" : "linha não encontrada")}", requestLogged);

        var rateLimited = events.Count(e => Template(e).StartsWith("Rate limit exceeded", StringComparison.Ordinal));
        run.Record("L-02", Area, "Rate limit estourado é registrado como Warning", "pelo menos 1 `Rate limit exceeded on {Path}`",
            $"{rateLimited} eventos", rateLimited > 0);

        var errors = events.Where(e => Level(e) is "Error" or "Fatal").Select(Template).Distinct().ToList();
        run.Record("L-03", Area, "Nenhum erro (Error/Fatal) no log durante o roteiro", "0 eventos",
            errors.Count == 0 ? "0 eventos" : $"{errors.Count} tipos: {string.Join("; ", errors)}", errors.Count == 0);

        var texts = lines.Select(line => line + "\n" + Unescape(line)).ToList();
        var leaked = context.Secrets
            .Where(secret => texts.Any(text => text.Contains(secret.Key, StringComparison.Ordinal)))
            .Select(secret => secret.Value)
            .Distinct()
            .ToList();
        run.Record("L-04", Area, "Nenhum dado sensível no log (senhas, tokens, CPF, texto do chat)",
            $"nenhum dos {context.Secrets.Count} valores usados no roteiro",
            leaked.Count == 0 ? "nenhum encontrado" : "encontrados: " + string.Join(", ", leaked), leaked.Count == 0);

        run.Note($"Logs conferidos: {lines.Count} linhas, {events.Count} eventos JSON, " +
            string.Join(", ", events.GroupBy(Level).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}")) + ".");
    }

    /// <summary>Nenhuma resposta, de nenhum cenário, trouxe stack trace ou nome de exceção.</summary>
    private static void CheckResponses(QaContext context)
    {
        var calls = context.Api.Calls;
        var exposed = calls.Where(c => InternalDetails.Any(d => c.Body.Contains(d, StringComparison.Ordinal))).ToList();
        var serverErrors = calls.Count(c => c.StatusCode >= 500);
        var withoutEnvelope = calls.Where(c => !c.IsEnvelope && !c.Path.StartsWith("/health", StringComparison.Ordinal)
            && c.Method != "OPTIONS" && !c.Path.StartsWith("/hubs", StringComparison.Ordinal)).ToList();

        context.Run.Record("S-20", "Segurança", "Nenhuma resposta da execução expõe stack trace, SQL ou nome de exceção",
            $"0 de {calls.Count} respostas", exposed.Count == 0 ? "0 respostas" : string.Join("; ", exposed.Select(c => $"{c.Method} {c.Path}")),
            exposed.Count == 0);

        context.Run.Record("S-21", "Segurança", "Nenhum 500 e toda resposta da API no envelope `ApiResponse`",
            "0 respostas 5xx; 0 fora do envelope",
            $"{serverErrors} respostas 5xx; {withoutEnvelope.Count} fora do envelope" +
            (withoutEnvelope.Count == 0 ? "" : ": " + string.Join("; ", withoutEnvelope.Select(c => $"{c.Method} {c.Path} {c.StatusCode}").Distinct())),
            serverErrors == 0 && withoutEnvelope.Count == 0);
    }

    private static async Task<(string Source, List<string>? Lines)> ReadLogsAsync(QaContext context)
    {
        if (context.Options.DockerContainer is { } container)
        {
            var since = context.StartedAt.AddSeconds(-1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var output = await RunDockerLogsAsync(container, since);

            return output is null
                ? ($"`docker logs {container}` falhou", null)
                : ($"`docker logs --since {since} {container}`", output);
        }

        if (context.Options.LogFile is { } file)
        {
            if (!File.Exists(file))
                return ($"arquivo {file} não existe", null);

            // Só o que foi escrito a partir do início do roteiro.
            var lines = (await ReadSharedAsync(file))
                .Where(line => Parse(line) is not { } e || Timestamp(e) is not { } at || at >= context.StartedAt.AddSeconds(-1))
                .ToList();

            return ($"arquivo `{Path.GetFileName(file)}`", lines);
        }

        return ("não informados (use --docker-logs ou --log-file)", null);
    }

    /// <summary>Lê o arquivo que a API ainda tem aberto para escrita.</summary>
    private static async Task<string[]> ReadSharedAsync(string file)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task<List<string>?> RunDockerLogsAsync(string container, string since)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("logs");
        start.ArgumentList.Add("--since");
        start.ArgumentList.Add(since);
        start.ArgumentList.Add(container);

        using var process = Process.Start(start);
        if (process is null)
            return null;

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            return null;

        return [.. (await stdout + "\n" + await stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static JsonElement? Parse(string line) =>
        line.StartsWith('{') ? Json.TryParse(line) : null;

    private static string Template(JsonElement e) =>
        e.TryGetProperty("MessageTemplate", out var template) ? template.GetString() ?? "" : "";

    private static string Level(JsonElement e) =>
        e.TryGetProperty("Level", out var level) ? level.GetString() ?? "Information" : "Information";

    private static DateTimeOffset? Timestamp(JsonElement e) =>
        e.TryGetProperty("Timestamp", out var at) && at.TryGetDateTimeOffset(out var value) ? value : null;

    private static string? Property(JsonElement e, string name) =>
        e.TryGetProperty("Properties", out var properties) && properties.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;

    /// <summary>A linha com os escapes do JSON desfeitos (<c>á</c> vira <c>á</c>).</summary>
    private static string Unescape(string line)
    {
        if (Parse(line) is not { } e)
            return line;

        var text = new StringBuilder();
        Collect(e, text);

        return text.ToString();
    }

    private static void Collect(JsonElement element, StringBuilder text)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, text);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, text);
                break;
            case JsonValueKind.String:
                text.Append(element.GetString()).Append('\n');
                break;
        }
    }
}
