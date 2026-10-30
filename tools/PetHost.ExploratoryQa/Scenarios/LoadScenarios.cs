using System.Globalization;
using System.Net;

namespace PetHost.ExploratoryQa.Scenarios;

/// <summary>
/// Carga leve: rajadas pequenas, do tamanho de um uso real, não um teste de estresse. Mede o
/// tempo de resposta e confere que o limite geral (120 por minuto) responde 429 no envelope.
/// </summary>
internal static class LoadScenarios
{
    private const string Area = "Carga leve";
    private const int Requests = 100;
    private const int Concurrency = 10;
    private const string Search = "/api/v1/listings?state=PR&city=Maring%C3%A1";

    /// <summary>
    /// 100 buscas, 10 de cada vez, com a sessão do Diego: o limite geral é por usuário logado,
    /// então as 100 cabem nos 120 por minuto.
    /// </summary>
    public static async Task RunAsync(QaContext context)
    {
        var token = context.Diego.AccessToken;
        var calls = new List<ApiCall>(Requests);
        using var gate = new SemaphoreSlim(Concurrency);

        var started = DateTimeOffset.UtcNow;
        var tasks = Enumerable.Range(0, Requests).Select(async _ =>
        {
            await gate.WaitAsync();
            try
            {
                return await context.Api.GetAsync(Search, token);
            }
            finally
            {
                gate.Release();
            }
        });
        calls.AddRange(await Task.WhenAll(tasks));
        var elapsed = DateTimeOffset.UtcNow - started;

        var ok = calls.Count(c => c.Status == HttpStatusCode.OK);
        var times = calls.Select(c => c.Elapsed.TotalMilliseconds).Order().ToList();
        var p50 = Percentile(times, 0.50);
        var p95 = Percentile(times, 0.95);

        context.Run.Record("C-01", Area, $"{Requests} buscas de cantinhos, {Concurrency} ao mesmo tempo, com login",
            "todas 200; p95 abaixo de 1 s",
            string.Create(CultureInfo.InvariantCulture, $"{ok}/{Requests} com 200; p50 {p50:0} ms, p95 {p95:0} ms, máx {times[^1]:0} ms"),
            ok == Requests && p95 < 1000);

        context.Run.Note(string.Create(CultureInfo.InvariantCulture,
            $"Carga leve (C-01): {Requests} requisições em {elapsed.TotalSeconds:0.0} s ({Requests / elapsed.TotalSeconds:0.0} req/s), " +
            $"mín {times[0]:0} ms, p50 {p50:0} ms, p95 {p95:0} ms, máx {times[^1]:0} ms."));
    }

    /// <summary>
    /// Sem login, o limite geral é por IP. Por último na execução: depois disso, este IP fica
    /// bloqueado até a janela de 1 minuto virar.
    /// </summary>
    public static async Task RunGlobalLimitAsync(QaContext context)
    {
        ApiCall? blocked = null;
        var sent = 0;

        while (sent < 200 && blocked is null)
        {
            var batch = await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ => context.Api.GetAsync(Search)));
            sent += batch.Length;
            blocked = batch.FirstOrDefault(c => c.Status == HttpStatusCode.TooManyRequests);
        }

        var retryAfter = blocked?.Headers.RetryAfter?.Delta;
        context.Run.Record("C-02", Area, "Rajada sem login até passar do limite geral (120 por minuto por IP)",
            "429 TOO_MANY_REQUESTS no envelope, com `Retry-After`",
            blocked is null
                ? $"nenhum 429 em {sent} requisições"
                : $"{blocked.Describe()} depois de {sent} requisições da rajada, Retry-After: {retryAfter?.TotalSeconds ?? 0} s",
            blocked is { ErrorCode: "TOO_MANY_REQUESTS", IsEnvelope: true } && retryAfter is not null);
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;

        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
