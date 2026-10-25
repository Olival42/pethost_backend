namespace PetHost.ExploratoryQa;

/// <summary>
/// Opções da linha de comando. As senhas vêm de variáveis de ambiente, para não ficarem no
/// histórico do terminal.
/// </summary>
internal sealed record QaOptions(
    Uri BaseUrl,
    string OutputPath,
    string? LogFile,
    string? DockerContainer,
    Uri FrontendOrigin,
    string TestDataPassword,
    string? AdminEmail,
    string? AdminPassword)
{
    public const string Usage = """
        Roteiro exploratório da API PetHost.

        Uso:
          dotnet run --project tools/PetHost.ExploratoryQa -- [opções]

        Opções:
          --base-url <url>        Endereço da API (padrão: http://localhost:8080)
          --out <arquivo>         Relatório em Markdown (padrão: docs/evidencias/exploratorio-<data>.md)
          --docker-logs <nome>    Confere os logs do container (ex.: pethost-api)
          --log-file <arquivo>    Confere os logs gravados neste arquivo
          --origin <url>          Origem liberada no CORS (padrão: http://localhost:3000)

        Variáveis de ambiente:
          PETHOST_QA_TEST_DATA_PASSWORD   Senha dos dados de teste (SEED_TEST_DATA_PASSWORD). Obrigatória.
          PETHOST_QA_ADMIN_EMAIL          E-mail do admin do seed (opcional: sem ele, os cenários de admin não rodam)
          PETHOST_QA_ADMIN_PASSWORD       Senha do admin do seed

        A API precisa estar com os dados de teste ligados (SEED_TEST_DATA_ENABLED=true). Entre duas
        execuções, espere 5 minutos: o roteiro estoura de propósito o limite de login por IP.
        """;

    public static QaOptions? Parse(string[] args, out string? error)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                error = $"Opção inválida: {args[i]}";
                return null;
            }

            values[args[i][2..]] = args[++i];
        }

        var password = Environment.GetEnvironmentVariable("PETHOST_QA_TEST_DATA_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            error = "Defina PETHOST_QA_TEST_DATA_PASSWORD com a senha dos dados de teste.";
            return null;
        }

        if (!Uri.TryCreate(values.GetValueOrDefault("base-url", "http://localhost:8080"), UriKind.Absolute, out var baseUrl)
            || !Uri.TryCreate(values.GetValueOrDefault("origin", "http://localhost:3000"), UriKind.Absolute, out var origin))
        {
            error = "--base-url e --origin precisam ser endereços completos (http://...).";
            return null;
        }

        error = null;

        return new QaOptions(
            baseUrl,
            values.GetValueOrDefault("out", $"docs/evidencias/exploratorio-{DateTime.Now:yyyy-MM-dd}.md"),
            values.GetValueOrDefault("log-file"),
            values.GetValueOrDefault("docker-logs"),
            origin,
            password,
            Environment.GetEnvironmentVariable("PETHOST_QA_ADMIN_EMAIL"),
            Environment.GetEnvironmentVariable("PETHOST_QA_ADMIN_PASSWORD"));
    }
}
