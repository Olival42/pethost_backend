using System.Net;
using System.Text;

namespace PetHost.ExploratoryQa.Scenarios;

/// <summary>
/// Entradas fora do caminho feliz: corpo quebrado, tipo errado, rota e método que não existem,
/// paginação e datas fora da faixa, texto hostil na busca, corpo gigante.
/// </summary>
internal static class EdgeCaseScenarios
{
    private const string Area = "Casos de borda";
    private const string NotificationSettings = "/api/v1/conversations/me/notification-settings";

    public static async Task RunAsync(QaContext context)
    {
        var api = context.Api;
        var run = context.Run;
        var token = context.Bruno.AccessToken;

        var malformed = await api.SendAsync(HttpMethod.Put, NotificationSettings,
            new StringContent("{\"unreadMessagesEmail\": tru", Encoding.UTF8, "application/json"), token);
        run.Expect("B-01", Area, "JSON quebrado no corpo", malformed, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var empty = await api.SendAsync(HttpMethod.Put, NotificationSettings, new { }, token);
        run.Expect("B-02", Area, "Corpo sem o campo obrigatório (`{}`)", empty, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var wrongType = await api.SendAsync(HttpMethod.Put, NotificationSettings, new { unreadMessagesEmail = "talvez" }, token);
        run.Expect("B-03", Area, "Campo com tipo errado (texto no lugar de booleano)", wrongType, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var plainText = await api.SendAsync(HttpMethod.Put, NotificationSettings,
            new StringContent("unreadMessagesEmail=false", Encoding.UTF8, "text/plain"), token);
        run.Expect("B-04", Area, "Content-Type que a API não aceita (`text/plain`)", plainText, HttpStatusCode.UnsupportedMediaType);

        var unknownRoute = await api.GetAsync("/api/v1/nao-existe", token);
        run.Expect("B-05", Area, "Rota que não existe", unknownRoute, HttpStatusCode.NotFound);

        var badId = await api.GetAsync("/api/v1/pets/nao-e-um-guid", token);
        run.Expect("B-06", Area, "Id fora do formato na rota (`/pets/nao-e-um-guid`)", badId, HttpStatusCode.NotFound);

        var wrongMethod = await api.SendAsync(HttpMethod.Delete, "/api/v1/listings", token: token);
        run.Expect("B-07", Area, "Método que a rota não tem (`DELETE /listings`)", wrongMethod, HttpStatusCode.MethodNotAllowed);

        const string Search = "/api/v1/listings?state=PR&city=Maring%C3%A1";

        var bigPage = await api.GetAsync(Search + "&pageSize=1000", token);
        run.Expect("B-08", Area, "`pageSize` acima do máximo (1000; máximo 100)", bigPage, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var pageZero = await api.GetAsync(Search + "&page=0", token);
        run.Expect("B-09", Area, "`page=0` (começa em 1)", pageZero, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var checkIn = DateTime.UtcNow.Date.AddDays(20);
        var inverted = await api.GetAsync($"{Search}&checkIn={checkIn.AddDays(3):yyyy-MM-dd}&checkOut={checkIn:yyyy-MM-dd}", token);
        run.Expect("B-10", Area, "Saída antes da chegada na busca por datas", inverted, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var past = await api.GetAsync($"{Search}&checkIn=2020-01-10&checkOut=2020-01-12", token);
        run.Expect("B-11", Area, "Datas no passado na busca", past, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var injection = await api.GetAsync(
            "/api/v1/listings?state=PR&city=" + Uri.EscapeDataString("' OR 1=1; DROP TABLE listing.listings; --"), token);
        var injectionEmpty = injection.Status == HttpStatusCode.OK && injection.Data.GetProperty("totalCount").GetInt32() == 0;
        run.Record("B-12", Area, "SQL injection no filtro de cidade", "200 com zero resultados (o texto é só um valor)",
            injection.Describe() + (injection.Status == HttpStatusCode.OK ? $", {injection.Data.GetProperty("totalCount").GetInt32()} resultados" : ""),
            injectionEmpty);

        var stillThere = await api.GetAsync(Search, token);
        var listingsLeft = stillThere.Status == HttpStatusCode.OK ? stillThere.Data.GetProperty("totalCount").GetInt32() : -1;
        run.Record("B-13", Area, "Os cantinhos continuam lá depois da tentativa de injection", "200 com os cantinhos de Maringá",
            stillThere.Describe() + $", {listingsLeft} cantinhos", listingsLeft > 0);

        var unicode = await api.GetAsync(
            "/api/v1/listings?state=PR&city=Maring%C3%A1&neighborhood=" + Uri.EscapeDataString("Zona 7 🐶 <script>alert(1)</script>"), token);
        run.Expect("B-14", Area, "Emoji e HTML no filtro de bairro", unicode, HttpStatusCode.OK);

        await HugeBodyAsync(context, token);
        await InvalidRegistrationAsync(context);
    }

    /// <summary>Corpo acima do limite do Kestrel (30 MB): tem que ser recusado sem 500.</summary>
    private static async Task HugeBodyAsync(QaContext context, string token)
    {
        var payload = new StringBuilder(31 * 1024 * 1024 + 64)
            .Append("{\"unreadMessagesEmail\":true,\"pad\":\"")
            .Append('a', 31 * 1024 * 1024)
            .Append("\"}")
            .ToString();

        try
        {
            var huge = await context.Api.SendAsync(HttpMethod.Put, NotificationSettings,
                new StringContent(payload, Encoding.UTF8, "application/json"), token);
            var refused = huge.StatusCode is 400 or 413 && huge.IsEnvelope;
            context.Run.Record("B-15", Area, "Corpo de 31 MB (acima do limite de 30 MB)", "400 ou 413 no envelope, sem 500",
                huge.Describe(), refused);
        }
        catch (HttpRequestException exception)
        {
            // O Kestrel pode fechar a conexão no meio do envio: a API recusou sem processar.
            context.Run.Record("B-15", Area, "Corpo de 31 MB (acima do limite de 30 MB)", "400 ou 413 no envelope, sem 500",
                "conexão encerrada pela API durante o envio (" + exception.Message + ")", passed: true);
        }
    }

    /// <summary>Cadastro com vários erros: todos voltam juntos num só 400.</summary>
    private static async Task InvalidRegistrationAsync(QaContext context)
    {
        var minor = DateTime.UtcNow.Date.AddYears(-15);
        var registration = await context.Api.PostAsync("/api/v1/owners/register", new
        {
            fullName = "A",
            email = "nao-e-email",
            password = "123",
            phone = "abc",
            birthDate = minor.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            cpf = "111.111.111-11",
            address = new { zipCode = "1", street = "", number = "", neighborhood = "", city = "", state = "XX" },
        });

        var fields = registration.ErrorFields.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        context.Run.Record("B-16", Area, "Cadastro de tutor com vários campos inválidos (menor de idade, CPF 111.111.111-11, senha fraca...)",
            "400 VALIDATION_ERROR com todos os campos de uma vez",
            registration.Describe() + $", {fields.Count} campos: {string.Join(", ", fields)}",
            registration.Status == HttpStatusCode.BadRequest && registration.ErrorCode == "VALIDATION_ERROR" && fields.Count >= 5);
    }
}
