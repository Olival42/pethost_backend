using System.Globalization;
using System.Net;
using System.Text;

namespace PetHost.ExploratoryQa;

internal enum QaOutcome
{
    Passed,
    Failed,
    Skipped,
}

/// <summary>Um cenário conferido: o que se esperava, o que a API devolveu e o veredito.</summary>
internal sealed record QaCheck(string Id, string Area, string Scenario, string Expected, string Actual, QaOutcome Outcome);

/// <summary>Os cenários da execução, na ordem em que rodaram, e o relatório em Markdown.</summary>
internal sealed class QaRun
{
    private readonly List<QaCheck> _checks = [];
    private readonly List<string> _notes = [];

    public IReadOnlyList<QaCheck> Checks => _checks;

    public bool HasFailures => _checks.Any(c => c.Outcome == QaOutcome.Failed);

    public void Record(string id, string area, string scenario, string expected, string actual, bool passed)
    {
        var check = new QaCheck(id, area, scenario, expected, actual, passed ? QaOutcome.Passed : QaOutcome.Failed);
        _checks.Add(check);
        Print(check);
    }

    public void Skip(string id, string area, string scenario, string reason)
    {
        var check = new QaCheck(id, area, scenario, "—", reason, QaOutcome.Skipped);
        _checks.Add(check);
        Print(check);
    }

    /// <summary>Confere o status e, quando informado, o código de erro do envelope.</summary>
    public bool Expect(string id, string area, string scenario, ApiCall call, HttpStatusCode status, string? code = null)
    {
        var passed = call.Status == status && call.IsEnvelope && (code is null || call.ErrorCode == code);
        var expected = code is null ? $"{(int)status}" : $"{(int)status} {code}";
        Record(id, area, scenario, expected + " no envelope", call.Describe(), passed);

        return passed;
    }

    /// <summary>Linha livre no fim do relatório (números da carga, por exemplo).</summary>
    public void Note(string text) => _notes.Add(text);

    public string ToMarkdown(Uri baseUrl, DateTimeOffset startedAt, TimeSpan duration, string logSource)
    {
        var passed = _checks.Count(c => c.Outcome == QaOutcome.Passed);
        var failed = _checks.Count(c => c.Outcome == QaOutcome.Failed);
        var skipped = _checks.Count(c => c.Outcome == QaOutcome.Skipped);
        var markdown = new StringBuilder();

        markdown.AppendLine("# Roteiro exploratório — execução contra a API no ar");
        markdown.AppendLine();
        markdown.AppendLine("Gerado por `tools/PetHost.ExploratoryQa` (Entrega 2, seção 6.1: casos de borda, segurança, tempo real, carga leve e logs).");
        markdown.AppendLine();
        markdown.AppendLine(CultureInfo.InvariantCulture, $"- **API:** {baseUrl}");
        markdown.AppendLine(CultureInfo.InvariantCulture, $"- **Início:** {startedAt.ToOffset(TimeSpan.FromHours(-3)):yyyy-MM-dd HH:mm:ss} (Brasília)");
        markdown.AppendLine(CultureInfo.InvariantCulture, $"- **Duração:** {duration.TotalSeconds:0} s");
        markdown.AppendLine(CultureInfo.InvariantCulture, $"- **Logs conferidos:** {logSource}");
        markdown.AppendLine(CultureInfo.InvariantCulture, $"- **Resultado:** {passed} passaram, {failed} falharam, {skipped} não rodaram");
        markdown.AppendLine();

        foreach (var area in _checks.Select(c => c.Area).Distinct())
        {
            markdown.AppendLine(CultureInfo.InvariantCulture, $"## {area}");
            markdown.AppendLine();
            markdown.AppendLine("| ID | Cenário | Esperado | Obtido | Resultado |");
            markdown.AppendLine("|---|---|---|---|---|");

            foreach (var check in _checks.Where(c => c.Area == area))
            {
                markdown.AppendLine(CultureInfo.InvariantCulture,
                    $"| {check.Id} | {Cell(check.Scenario)} | {Cell(check.Expected)} | {Cell(check.Actual)} | {Verdict(check.Outcome)} |");
            }

            markdown.AppendLine();
        }

        if (_notes.Count > 0)
        {
            markdown.AppendLine("## Observações");
            markdown.AppendLine();

            foreach (var note in _notes)
                markdown.AppendLine(CultureInfo.InvariantCulture, $"- {note}");

            markdown.AppendLine();
        }

        return markdown.ToString();
    }

    private static string Verdict(QaOutcome outcome) => outcome switch
    {
        QaOutcome.Passed => "✅",
        QaOutcome.Failed => "❌",
        _ => "⏭️",
    };

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static void Print(QaCheck check)
    {
        var mark = check.Outcome switch
        {
            QaOutcome.Passed => "OK  ",
            QaOutcome.Failed => "FAIL",
            _ => "SKIP",
        };

        Console.WriteLine($"[{mark}] {check.Id} {check.Scenario} -> {check.Actual}");
    }
}
