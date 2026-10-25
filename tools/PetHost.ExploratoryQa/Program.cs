using System.Text;
using PetHost.ExploratoryQa;
using PetHost.ExploratoryQa.Scenarios;

Console.OutputEncoding = Encoding.UTF8;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine(QaOptions.Usage);
    return 0;
}

var options = QaOptions.Parse(args, out var error);
if (options is null)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(QaOptions.Usage);
    return 2;
}

using var http = new HttpClient { BaseAddress = options.BaseUrl, Timeout = TimeSpan.FromSeconds(60) };
var run = new QaRun();
var context = new QaContext(new ApiClient(http), run, options);

Console.WriteLine($"Roteiro exploratório contra {options.BaseUrl}");

// A ordem importa: os dois últimos estouram de propósito limites por IP (login e o geral).
var areas = new (string Name, Func<QaContext, Task> Run)[]
{
    ("Casos de borda", EdgeCaseScenarios.RunAsync),
    ("Segurança", SecurityScenarios.RunAsync),
    ("Chat e tempo real", RealtimeScenarios.RunAsync),
    ("Carga leve", LoadScenarios.RunAsync),
    ("Força bruta no login", SecurityScenarios.RunBruteForceAsync),
    ("Limite geral", LoadScenarios.RunGlobalLimitAsync),
};

try
{
    if (await PreparationScenarios.RunAsync(context))
    {
        foreach (var (name, runArea) in areas)
        {
            try
            {
                await runArea(context);
            }
            catch (Exception exception)
            {
                run.Record("X", name, $"{name}: o roteiro parou com erro", "sem erro", exception.GetType().Name + ": " + exception.Message, passed: false);
            }
        }

        await LogScenarios.RunAsync(context);
    }
}
catch (HttpRequestException exception)
{
    run.Record("P-01", "Preparação", "API no ar", "responde", "sem resposta: " + exception.Message, passed: false);
}

var duration = DateTimeOffset.UtcNow - context.StartedAt;
var logSource = options.DockerContainer is { } container ? $"`docker logs {container}`"
    : options.LogFile is { } file ? $"arquivo `{Path.GetFileName(file)}`"
    : "não informados";
var report = run.ToMarkdown(options.BaseUrl, context.StartedAt, duration, logSource);

var output = Path.GetFullPath(options.OutputPath);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

var failed = run.Checks.Count(c => c.Outcome == QaOutcome.Failed);
Console.WriteLine();
Console.WriteLine($"{run.Checks.Count} cenários, {failed} falharam. Relatório: {output}");

return run.HasFailures ? 1 : 0;
