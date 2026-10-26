using System.Collections.Concurrent;

namespace PetHost.ExploratoryQa;

/// <summary>Uma sessão aberta pelo roteiro.</summary>
internal sealed record Session(string Email, string Role, string AccessToken, string RefreshToken, Guid UserId);

/// <summary>
/// O que as áreas do roteiro compartilham: o cliente, os resultados, as sessões dos dados de
/// teste e tudo que não pode aparecer nos logs da API.
/// </summary>
internal sealed class QaContext(ApiClient api, QaRun run, QaOptions options)
{
    // Dados de teste do seed (src/Host/PetHost.Api/Seed/TestDataCatalog.cs).
    public const string OwnerAnaEmail = "tutora.ana@pethost.local";
    public const string OwnerBrunoEmail = "tutor.bruno@pethost.local";
    public const string HostCarlaEmail = "anfitria.carla@pethost.local";
    public const string HostDiegoEmail = "anfitriao.diego@pethost.local";
    public const string OwnerAnaCpf = "52998224725";

    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public ApiClient Api => api;

    public QaRun Run => run;

    public QaOptions Options => options;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public Session Ana { get; set; } = null!;

    public Session Bruno { get; set; } = null!;

    public Session Carla { get; set; } = null!;

    public Session Diego { get; set; } = null!;

    public Session? Admin { get; set; }

    /// <summary>Senhas, tokens, CPFs e textos de mensagem usados na execução: valor e tipo.</summary>
    public IReadOnlyDictionary<string, string> Secrets => _secrets;

    public void AddSecret(string kind, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _secrets.TryAdd(value, kind);
    }

    public Task<ApiCall> LoginAsync(string email, string password, string role) =>
        api.PostAsync("/api/v1/auth/sessions/login", new { email, password, role });

    /// <summary>Lê a sessão de uma resposta de login, cadastro ou refresh.</summary>
    public Session ReadSession(ApiCall call, string role)
    {
        var data = call.Data;
        var session = new Session(
            data.GetProperty("user").GetProperty("email").GetString()!,
            role,
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("refreshToken").GetString()!,
            data.GetProperty("user").GetProperty("id").GetGuid());

        AddSecret("access token", session.AccessToken);
        AddSecret("refresh token", session.RefreshToken);

        return session;
    }
}
