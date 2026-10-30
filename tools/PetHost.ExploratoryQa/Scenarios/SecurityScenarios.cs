using System.Net;
using System.Text;

namespace PetHost.ExploratoryQa.Scenarios;

/// <summary>
/// Autenticação, autorização, acesso a dado de outra pessoa (IDOR), enumeração de contas,
/// sessão (refresh e logout), CORS e força bruta no login.
/// </summary>
internal static class SecurityScenarios
{
    private const string Area = "Segurança";
    private const string Me = "/api/v1/users/me";

    public static async Task RunAsync(QaContext context)
    {
        await TokensAsync(context);
        await RolesAsync(context);
        await OtherPeoplesDataAsync(context);
        await AccountEnumerationAsync(context);
        await SessionAsync(context);
        await CorsAsync(context);
    }

    private static async Task TokensAsync(QaContext context)
    {
        var api = context.Api;
        var run = context.Run;

        var anonymous = await api.GetAsync(Me);
        run.Expect("S-01", Area, "Rota logada sem token", anonymous, HttpStatusCode.Unauthorized);

        var escalated = Jwt.WithClaim(context.Ana.AccessToken, "role", "admin");
        var tampered = await api.GetAsync("/api/v1/owners", escalated);
        run.Expect("S-02", Area, "Token com o papel trocado para admin (assinatura não confere)", tampered, HttpStatusCode.Unauthorized);

        var unsigned = await api.GetAsync(Me, Jwt.Unsigned(context.Ana.AccessToken));
        run.Expect("S-03", Area, "Token sem assinatura (`alg: none`)", unsigned, HttpStatusCode.Unauthorized);

        var garbage = await api.GetAsync(Me, "nao-e-um-jwt");
        run.Expect("S-04", Area, "Token que não é JWT", garbage, HttpStatusCode.Unauthorized);
    }

    private static async Task RolesAsync(QaContext context)
    {
        var ownerOnAdmin = await context.Api.GetAsync("/api/v1/owners", context.Ana.AccessToken);
        context.Run.Expect("S-05", Area, "Tutor numa rota de admin (`GET /owners`)", ownerOnAdmin, HttpStatusCode.Forbidden);

        var hostOnOwner = await context.Api.PostAsync("/api/v1/conversations", new { listingId = Guid.NewGuid() }, context.Carla.AccessToken);
        context.Run.Expect("S-06", Area, "Anfitrião numa rota de tutor (`POST /conversations`)", hostOnOwner, HttpStatusCode.Forbidden);
    }

    /// <summary>Bruno tenta ler e alterar o pet da Ana.</summary>
    private static async Task OtherPeoplesDataAsync(QaContext context)
    {
        var api = context.Api;
        var run = context.Run;

        var anaPets = await api.GetAsync("/api/v1/pets/me", context.Ana.AccessToken);
        var pet = anaPets.Data.GetProperty("pets").EnumerateArray().First();
        var petId = pet.GetProperty("id").GetGuid();
        var name = pet.GetProperty("name").GetString();

        var read = await api.GetAsync($"/api/v1/pets/{petId}", context.Bruno.AccessToken);
        run.Expect("S-07", Area, "Tutor lê o pet de outro tutor", read, HttpStatusCode.NotFound, "PET_NOT_FOUND");

        var change = await api.SendAsync(HttpMethod.Patch, $"/api/v1/pets/{petId}", new { name = "Invadido" }, context.Bruno.AccessToken);
        run.Expect("S-08", Area, "Tutor altera o pet de outro tutor", change, HttpStatusCode.NotFound, "PET_NOT_FOUND");

        var after = await api.GetAsync($"/api/v1/pets/{petId}", context.Ana.AccessToken);
        var unchanged = after.Status == HttpStatusCode.OK ? after.Data.GetProperty("name").GetString() : null;
        run.Record("S-09", Area, "O pet continua com o nome de antes", $"\"{name}\"", $"\"{unchanged}\"", unchanged == name);
    }

    /// <summary>Login e "esqueci a senha" não contam quem tem cadastro.</summary>
    private static async Task AccountEnumerationAsync(QaContext context)
    {
        var run = context.Run;
        const string WrongPassword = "Senha-Errada-Exploratorio-1";
        context.AddSecret("senha", WrongPassword);

        var wrongPassword = await context.LoginAsync(QaContext.OwnerAnaEmail, WrongPassword, "owner");
        var unknownEmail = await context.LoginAsync("ninguem.exploratorio@pethost.local", WrongPassword, "owner");
        run.Record("S-10", Area, "Login: senha errada e e-mail inexistente respondem igual",
            "mesmo status, código e mensagem (401 AUTH_INVALID_CREDENTIALS)",
            $"senha errada: {wrongPassword.Describe()}; e-mail inexistente: {unknownEmail.Describe()}",
            wrongPassword.Status == HttpStatusCode.Unauthorized
            && wrongPassword.ErrorCode == "AUTH_INVALID_CREDENTIALS"
            && wrongPassword.StatusCode == unknownEmail.StatusCode
            && wrongPassword.ErrorCode == unknownEmail.ErrorCode
            && wrongPassword.ErrorMessage == unknownEmail.ErrorMessage);

        var wrongRole = await context.LoginAsync(QaContext.OwnerAnaEmail, context.Options.TestDataPassword, "host");
        run.Expect("S-11", Area, "Login com a senha certa e o papel de outra conta (tutora como anfitriã)", wrongRole,
            HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");

        var injection = await context.LoginAsync("' OR '1'='1' --", "' OR '1'='1' --", "owner");
        run.Expect("S-12", Area, "SQL injection no e-mail e na senha do login", injection, HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");

        var existing = await context.Api.PostAsync("/api/v1/auth/password/forgot", new { email = QaContext.OwnerBrunoEmail, role = "owner" });
        var missing = await context.Api.PostAsync("/api/v1/auth/password/forgot", new { email = "ninguem.exploratorio@pethost.local", role = "owner" });
        run.Record("S-13", Area, "Esqueci a senha: conta existente e inexistente respondem igual", "200 nas duas, mesmo `data`",
            $"existente: {existing.Describe()}; inexistente: {missing.Describe()}",
            existing.Status == HttpStatusCode.OK
            && missing.Status == HttpStatusCode.OK
            && existing.Data.GetRawText() == missing.Data.GetRawText());
    }

    /// <summary>Refresh token de uso único e logout que revoga.</summary>
    private static async Task SessionAsync(QaContext context)
    {
        var api = context.Api;
        var run = context.Run;
        var original = context.Bruno.RefreshToken;

        var refreshed = await api.PostAsync("/api/v1/auth/sessions/refresh", new { refreshToken = original });
        run.Expect("S-14", Area, "Refresh: troca o refresh token por um par novo", refreshed, HttpStatusCode.OK);
        if (refreshed.Status != HttpStatusCode.OK)
            return;

        context.Bruno = context.ReadSession(refreshed, "owner");

        var reused = await api.PostAsync("/api/v1/auth/sessions/refresh", new { refreshToken = original });
        run.Expect("S-15", Area, "Refresh com o token antigo, já trocado", reused, HttpStatusCode.Unauthorized, "AUTH_REFRESH_TOKEN_INVALID");

        var logout = await api.PostAsync("/api/v1/auth/sessions/logout", new { refreshToken = context.Bruno.RefreshToken });
        var afterLogout = await api.PostAsync("/api/v1/auth/sessions/refresh", new { refreshToken = context.Bruno.RefreshToken });
        run.Record("S-16", Area, "Logout invalida o refresh token", "logout 200; refresh depois 401 AUTH_REFRESH_TOKEN_INVALID",
            $"logout: {logout.Describe()}; refresh: {afterLogout.Describe()}",
            logout.Status == HttpStatusCode.OK && afterLogout.ErrorCode == "AUTH_REFRESH_TOKEN_INVALID");
    }

    private static async Task CorsAsync(QaContext context)
    {
        var allowed = context.Options.FrontendOrigin.GetLeftPart(UriPartial.Authority);

        var fromFront = await PreflightAsync(context, allowed);
        var frontHeader = AllowedOrigin(fromFront);
        context.Run.Record("S-17", Area, $"CORS: preflight vindo do front ({allowed})", $"`Access-Control-Allow-Origin: {allowed}`",
            $"{fromFront.StatusCode}, Allow-Origin: {frontHeader ?? "ausente"}", frontHeader == allowed);

        var fromElsewhere = await PreflightAsync(context, "https://site-malicioso.example");
        var otherHeader = AllowedOrigin(fromElsewhere);
        context.Run.Record("S-18", Area, "CORS: preflight vindo de outra origem", "sem `Access-Control-Allow-Origin`",
            $"{fromElsewhere.StatusCode}, Allow-Origin: {otherHeader ?? "ausente"}", otherHeader is null);
    }

    /// <summary>
    /// Por último: estoura o limite de login por IP (10 a cada 5 minutos). Depois disso, login
    /// deste IP só volta em 5 minutos.
    /// </summary>
    public static async Task RunBruteForceAsync(QaContext context)
    {
        ApiCall? blocked = null;
        var attempts = 0;

        while (attempts < 15 && blocked is null)
        {
            attempts++;
            var login = await context.LoginAsync(QaContext.OwnerAnaEmail, "Forca-Bruta-" + attempts, "owner");
            if (login.Status == HttpStatusCode.TooManyRequests)
                blocked = login;
        }

        var retryAfter = blocked?.Headers.RetryAfter?.Delta;
        context.Run.Record("S-19", Area, "Força bruta no login: tentativas seguidas com senha errada",
            "429 TOO_MANY_REQUESTS no envelope, com `Retry-After`, até a 11ª tentativa da janela",
            blocked is null
                ? $"nenhum 429 em {attempts} tentativas"
                : $"{blocked.Describe()} na tentativa {attempts} da rajada (os logins anteriores do roteiro contam na mesma janela), Retry-After: {retryAfter?.TotalSeconds ?? 0} s",
            blocked is { ErrorCode: "TOO_MANY_REQUESTS", IsEnvelope: true } && retryAfter is not null);
    }

    private static Task<ApiCall> PreflightAsync(QaContext context, string origin) =>
        context.Api.SendAsync(HttpMethod.Options, Me, configure: request =>
        {
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "GET");
            request.Headers.Add("Access-Control-Request-Headers", "authorization");
        });

    private static string? AllowedOrigin(ApiCall call) =>
        call.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.FirstOrDefault() : null;
}

/// <summary>Monta tokens adulterados a partir de um token de verdade.</summary>
internal static class Jwt
{
    /// <summary>Troca uma claim do payload e mantém a assinatura original.</summary>
    public static string WithClaim(string token, string claim, string value)
    {
        var parts = token.Split('.');
        var payload = System.Text.Json.Nodes.JsonNode.Parse(Decode(parts[1]))!.AsObject();
        payload[claim] = value;

        return $"{parts[0]}.{Encode(payload.ToJsonString())}.{parts[2]}";
    }

    /// <summary>O mesmo payload com <c>alg: none</c> e sem assinatura.</summary>
    public static string Unsigned(string token)
    {
        var parts = token.Split('.');

        return $"{Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{parts[1]}.";
    }

    private static string Decode(string segment)
    {
        var base64 = segment.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static string Encode(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
