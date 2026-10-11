using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Hosts.IntegrationTests.HostApplications;

/// <summary>
/// Anfitrião pessoa jurídica ponta a ponta, e a restrição 9 entre os módulos: o mesmo CPF só
/// em tutor e anfitrião do <b>mesmo e-mail</b>.
/// </summary>
[Collection(HostsApiCollection.Name)]
public sealed class CompanyAndCpfRuleTests(HostsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ApplicationRoute = "/api/v1/hosts/me/application";
    private const string Password = "Nova@Senha123";
    private const string Cpf = "529.982.247-25";

    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        _client = fixture.CreateClient();
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    // ----- pessoa jurídica -----

    [Fact]
    public async Task Company_Should_BecomeACompanyHost_When_AdminApproves()
    {
        var host = await RegisterHostAccountAsync("contato@petfeliz.com");

        var started = await StartCompanyAsync(host, "11.222.333/0001-81");
        started.StatusCode.Should().Be(HttpStatusCode.Created);
        var company = Data(started).GetProperty("company");
        company.GetProperty("cnpj").GetString().Should().Be("11222333000181");
        company.GetProperty("address").GetProperty("city").GetString().Should().Be("Maringá");

        await UploadAllAsync(host);
        var applicationId = Data(await SendAsync(HttpMethod.Post, $"{ApplicationRoute}/submission", host)).GetProperty("id").GetGuid();

        var admin = await AdminTokenAsync();
        var detail = Data(await SendAsync(HttpMethod.Get, $"/api/v1/host-applications/{applicationId}", admin));
        detail.GetProperty("personType").GetString().Should().Be("pj");
        detail.GetProperty("company").GetProperty("legalName").GetString().Should().Be("Pet Feliz Hospedagem LTDA");

        (await SendAsync(HttpMethod.Post, $"/api/v1/host-applications/{applicationId}/approval", admin, new { reason = "CNPJ ativo, representante no QSA" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var me = Data(await SendAsync(HttpMethod.Get, "/api/v1/hosts/me", host));
        me.GetProperty("personType").GetString().Should().Be("pj");
        me.GetProperty("cpf").GetString().Should().Be("52998224725", "o CPF é do representante");
        me.GetProperty("company").GetProperty("tradeName").GetString().Should().Be("Pet Feliz");
    }

    [Fact]
    public async Task Company_Should_Return400WithEveryCompanyField_When_DataIsMissing()
    {
        var host = await RegisterHostAccountAsync("contato@petfeliz.com");

        var response = await SendAsync(HttpMethod.Post, ApplicationRoute, host,
            new { personType = "PJ", cpf = Cpf, documentType = "cnh", acceptNotice = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(response).Should().Contain(["cnpj", "legalName", "tradeName", "companyAddress.zipCode"]);
    }

    [Fact]
    public async Task Company_Should_Return409_When_AnotherAccountHasAnOpenApplicationWithTheCnpj()
    {
        await StartCompanyAsync(await RegisterHostAccountAsync("contato@petfeliz.com"), "11.222.333/0001-81");
        var other = await RegisterHostAccountAsync("outro@petfeliz.com");

        var response = await StartCompanyAsync(other, "11.222.333/0001-81", cpf: "111.444.777-35");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(response).Should().Be("HOST_CNPJ_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Individual_Should_Return400_When_CompanyDataIsSent()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");

        var response = await SendAsync(HttpMethod.Post, ApplicationRoute, host,
            new { cpf = Cpf, documentType = "cnh", acceptNotice = true, cnpj = "11.222.333/0001-81" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(response).Should().Equal("cnpj");
    }

    // ----- CPF entre papéis (restrição 9) -----

    [Fact]
    public async Task Host_Should_UseTheCpfOfItsOwnOwnerAccount_When_ItIsTheSameEmail()
    {
        (await RegisterOwnerAsync("camila@exemplo.com", Cpf)).StatusCode.Should().Be(HttpStatusCode.Created);
        var host = await RegisterHostAccountAsync("camila@exemplo.com");

        var response = await StartIndividualAsync(host, Cpf);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "a mesma pessoa pode ser tutora e anfitriã com o mesmo CPF");
    }

    [Fact]
    public async Task Host_Should_Return409_When_TheCpfBelongsToAnOwnerOfAnotherEmail()
    {
        (await RegisterOwnerAsync("camila@exemplo.com", Cpf)).StatusCode.Should().Be(HttpStatusCode.Created);
        var host = await RegisterHostAccountAsync("golpista@exemplo.com");

        var response = await StartIndividualAsync(host, Cpf);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(response).Should().Be("HOST_CPF_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Owner_Should_Return409_When_TheCpfIsInAHostApplicationOfAnotherEmail()
    {
        await StartIndividualAsync(await RegisterHostAccountAsync("cida@exemplo.com"), Cpf);

        var response = await RegisterOwnerAsync("outra@exemplo.com", Cpf);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(response).Should().Be("OWNER_CPF_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Owner_Should_RegisterWithTheCpfOfItsOwnHostAccount_When_ItIsTheSameEmail()
    {
        await StartIndividualAsync(await RegisterHostAccountAsync("cida@exemplo.com"), Cpf);

        var response = await RegisterOwnerAsync("cida@exemplo.com", Cpf);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ----- helpers -----

    private async Task<string> RegisterHostAccountAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "Dona Cida",
            email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1968-03-15",
            address = new { zipCode = "87033-000", street = "Rua das Palmeiras", number = "100", neighborhood = "Jardim Alvorada", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(response).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> RegisterOwnerAsync(string email, string cpf) =>
        _client.PostAsJsonAsync("/api/v1/owners/register", new
        {
            fullName = "Camila Souza",
            email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf,
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);

    private Task<HttpResponseMessage> StartIndividualAsync(string token, string cpf) =>
        SendAsync(HttpMethod.Post, ApplicationRoute, token, new { cpf, documentType = "cnh", acceptNotice = true });

    private Task<HttpResponseMessage> StartCompanyAsync(string token, string cnpj, string cpf = Cpf) =>
        SendAsync(HttpMethod.Post, ApplicationRoute, token, new
        {
            personType = "PJ",
            cpf,
            documentType = "cnh",
            acceptNotice = true,
            cnpj,
            legalName = "Pet Feliz Hospedagem LTDA",
            tradeName = "Pet Feliz",
            companyAddress = new { zipCode = "87013-000", street = "Av. Brasil", number = "1500", neighborhood = "Centro", city = "Maringá", state = "PR" },
        });

    private async Task UploadAllAsync(string token)
    {
        var form = new MultipartFormDataContent();
        foreach (var (kind, variant) in new[] { ("front", 11), ("back", 12), ("selfie", 13) })
        {
            var file = new ByteArrayContent(TestImages.PngVariant(variant));
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, kind, $"{kind}.png");
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, $"{ApplicationRoute}/documents") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(request, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = HostsApiFixture.AdminEmail, password = HostsApiFixture.AdminPassword, role = "admin" },
            Ct);

        return Data(response).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

    private static JsonElement Read(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), Json);

    private static JsonElement Data(HttpResponseMessage response) => Read(response).GetProperty("data");

    private static string? Code(HttpResponseMessage response) => Read(response).GetProperty("error").GetProperty("code").GetString();

    private static IEnumerable<string?> Fields(HttpResponseMessage response) =>
        Read(response).GetProperty("error").GetProperty("details").EnumerateArray().Select(d => d.GetProperty("field").GetString());
}
