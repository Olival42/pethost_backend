using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Hosts.Application.HostApplications.PurgeHostApplications;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Shared.Kernel.Messaging;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Hosts.IntegrationTests.HostApplications;

/// <summary>
/// A verificação do anfitrião ponta a ponta pela API: solicitação, fotos no bucket privado,
/// fila do admin, aprovação/recusa, prazo de guarda e trilha.
/// </summary>
[Collection(HostsApiCollection.Name)]
public sealed class HostApplicationFlowTests(HostsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string MeRoute = "/api/v1/hosts/me";
    private const string ApplicationRoute = "/api/v1/hosts/me/application";
    private const string ReviewRoute = "/api/v1/host-applications";
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

    // ----- fluxo feliz -----

    [Fact]
    public async Task Flow_Should_CreateTheVerifiedHost_When_AdminApprovesTheApplication()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");

        // Antes: pode começar, ainda não é anfitriã.
        var mine = Data(await SendAsync(HttpMethod.Get, ApplicationRoute, host));
        mine.GetProperty("canStartNew").GetBoolean().Should().BeTrue();
        mine.TryGetProperty("application", out _).Should().BeFalse("nunca abriu uma solicitação");
        (await SendAsync(HttpMethod.Get, MeRoute, host)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var started = await StartAsync(host);
        started.StatusCode.Should().Be(HttpStatusCode.Created);
        started.Headers.Location!.OriginalString.Should().Be(ApplicationRoute);

        var privateBefore = await fixture.Bucket.CountPrivateAsync();
        var publicBefore = await fixture.Bucket.CountAsync();
        await UploadAllAsync(host);
        (await fixture.Bucket.CountPrivateAsync()).Should().Be(privateBefore + 3);
        (await fixture.Bucket.CountAsync()).Should().Be(publicBefore, "documento nunca vai para o bucket público");

        var submitted = await SendAsync(HttpMethod.Post, $"{ApplicationRoute}/submission", host);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        var applicationId = Data(submitted).GetProperty("id").GetGuid();

        // O admin vê a fila, o detalhe e a selfie.
        var admin = await AdminTokenAsync();
        var queue = Data(await SendAsync(HttpMethod.Get, ReviewRoute, admin));
        queue.EnumerateArray().Should().ContainSingle(a => a.GetProperty("id").GetGuid() == applicationId);

        var detail = Data(await SendAsync(HttpMethod.Get, $"{ReviewRoute}/{applicationId}", admin));
        detail.GetProperty("user").GetProperty("fullName").GetString().Should().Be("Dona Cida");
        detail.GetProperty("documents").EnumerateArray().Should().HaveCount(3);

        var selfie = await SendAsync(HttpMethod.Get, $"{ReviewRoute}/{applicationId}/documents/selfie", admin);
        selfie.StatusCode.Should().Be(HttpStatusCode.OK);
        selfie.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        selfie.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await selfie.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(TestImages.PngVariant(3));

        // Aprova: nasce o anfitrião e as fotos somem (prazo de guarda zero).
        var approved = await SendAsync(HttpMethod.Post, $"{ReviewRoute}/{applicationId}/approval", admin, new { reason = "Documento e selfie conferidos" });
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(approved).GetProperty("status").GetString().Should().Be("approved");

        var me = Data(await SendAsync(HttpMethod.Get, MeRoute, host));
        me.GetProperty("cpf").GetString().Should().Be("52998224725");
        me.GetProperty("stripeConnected").GetBoolean().Should().BeFalse();

        (await fixture.Bucket.CountPrivateAsync()).Should().Be(privateBefore, "as fotos são apagadas na decisão");
        var gone = await SendAsync(HttpMethod.Get, $"{ReviewRoute}/{applicationId}/documents/selfie", admin);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(gone).Should().Be("HOST_APPLICATION_DOCUMENT_NOT_FOUND");

        // A trilha registrou quem viu a selfie e quem aprovou.
        var trail = await AuditAsync(admin, $"?targetType=host_application&targetId={applicationId}");
        trail.Select(e => e.GetProperty("action").GetString()).Should().Contain(
            ["host_application.started", "host_application.submitted", "host_application.document_viewed", "host_application.approved"]);
    }

    // ----- privacidade -----

    [Fact]
    public async Task PrivateBucket_Should_NotServeThePhotos_When_OpenedWithoutCredentials()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);
        await UploadAsync(host, "front", TestImages.PngVariant(1));

        var keys = await fixture.Bucket.ListPrivateKeysAsync();
        using var anonymous = new HttpClient();
        var response = await anonymous.GetAsync(fixture.Bucket.PrivateObjectUrl(keys[^1]), Ct);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK, "o bucket de documentos não tem leitura pública");
    }

    [Fact]
    public async Task OwnApplication_Should_NotExposeAnyPhotoLink_When_ReadByTheHost()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);
        await UploadAsync(host, "front", TestImages.PngVariant(1));

        var raw = await (await SendAsync(HttpMethod.Get, ApplicationRoute, host)).Content.ReadAsStringAsync(Ct);

        raw.Should().NotContain("host-applications/").And.NotContain("http", "a pessoa não recebe link nem chave das fotos");
    }

    [Fact]
    public async Task ReviewRoutes_Should_Return403_When_CalledByAHost()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");

        (await SendAsync(HttpMethod.Get, ReviewRoute, host)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- regras -----

    [Fact]
    public async Task Submit_Should_Return422WithTheMissingPhotos_When_SomeAreMissing()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);
        await UploadAsync(host, "front", TestImages.PngVariant(1));

        var response = await SendAsync(HttpMethod.Post, $"{ApplicationRoute}/submission", host);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(response).Should().Be("HOST_APPLICATION_DOCUMENTS_MISSING");
    }

    [Fact]
    public async Task Start_Should_Return400WithEveryField_When_TheBodyIsInvalid()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");

        var response = await SendAsync(HttpMethod.Post, ApplicationRoute, host, new { cpf = "123", documentType = "passport", acceptNotice = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(response).Should().BeEquivalentTo(["cpf", "documentType", "acceptNotice"]);
    }

    [Fact]
    public async Task Start_Should_Return409_When_AnotherAccountHasAnOpenApplicationWithTheCpf()
    {
        await StartAsync(await RegisterHostAccountAsync("cida@exemplo.com"));
        var other = await RegisterHostAccountAsync("outra@exemplo.com");

        var response = await StartAsync(other);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(response).Should().Be("HOST_CPF_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Upload_Should_Return409_When_TheSameImageGoesToTwoSlots()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);
        await UploadAsync(host, "front", TestImages.PngVariant(1));

        var response = await UploadRawAsync(host, "back", TestImages.PngVariant(1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(response).Should().Be("HOST_APPLICATION_DOCUMENT_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Upload_Should_Return400_When_TheFileIsNotAnImage()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);

        var response = await UploadRawAsync(host, "front", TestImages.NotAnImage);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(response).Should().Equal("front");
    }

    [Fact]
    public async Task Upload_Should_StoreTheThreePhotos_When_TheyComeInOneRequest()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);

        var response = await UploadRawAsync(
            host,
            ("front", TestImages.PngVariant(1)),
            ("back", TestImages.PngVariant(2)),
            ("selfie", TestImages.PngVariant(3)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var application = Data(response);
        application.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("kind").GetString())
            .Should().BeEquivalentTo(["front", "back", "selfie"]);
        application.GetProperty("missingDocuments").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_Should_KeepNothing_When_OneOfThePhotosIsInvalid()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);

        var response = await UploadRawAsync(
            host,
            ("front", TestImages.PngVariant(1)),
            ("back", TestImages.NotAnImage),
            ("selfie", TestImages.PngVariant(3)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(response).Should().Equal("back");
        var mine = Data(await SendAsync(HttpMethod.Get, ApplicationRoute, host)).GetProperty("application");
        mine.GetProperty("documents").EnumerateArray().Should().BeEmpty("tudo ou nada");
    }

    [Fact]
    public async Task Upload_Should_Return400_When_NoPhotoIsSent()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);

        var response = await UploadRawAsync(host, ("file", TestImages.PngVariant(1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(response).Should().Equal("documents");
    }

    [Fact]
    public async Task Cancel_Should_DeleteThePhotos_When_ThePersonGivesUp()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);
        var before = await fixture.Bucket.CountPrivateAsync();
        await UploadAsync(host, "front", TestImages.PngVariant(1));

        var response = await SendAsync(HttpMethod.Delete, ApplicationRoute, host);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(response).GetProperty("status").GetString().Should().Be("cancelled");
        (await fixture.Bucket.CountPrivateAsync()).Should().Be(before);
    }

    // ----- recusa -----

    [Fact]
    public async Task Reject_Should_LetThePersonTryAgain_When_TheReasonIsCommon()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        var applicationId = await SubmitCompleteApplicationAsync(host);
        var admin = await AdminTokenAsync();

        var rejected = await SendAsync(HttpMethod.Post, $"{ReviewRoute}/{applicationId}/rejection", admin,
            new { rejectionReason = "document_illegible", reason = "Foto escura" });
        rejected.StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = Data(await SendAsync(HttpMethod.Get, ApplicationRoute, host));
        mine.GetProperty("application").GetProperty("rejectionReason").GetString().Should().Be("document_illegible");
        mine.GetProperty("application").TryGetProperty("reviewNotes", out _).Should().BeFalse("a nota do admin é interna");
        mine.GetProperty("canStartNew").GetBoolean().Should().BeTrue();
        mine.GetProperty("remainingAttempts").GetInt32().Should().Be(2);

        var again = await StartAsync(host);
        again.StatusCode.Should().Be(HttpStatusCode.Created);
        Data(again).GetProperty("attemptNumber").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Reject_Should_BlockNewApplications_When_FraudIsSuspected()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        var applicationId = await SubmitCompleteApplicationAsync(host);

        await SendAsync(HttpMethod.Post, $"{ReviewRoute}/{applicationId}/rejection", await AdminTokenAsync(),
            new { rejectionReason = "suspected_fraud", reason = "Documento adulterado" });

        var again = await StartAsync(host);
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(again).Should().Be("HOST_APPLICATION_BLOCKED");
    }

    [Fact]
    public async Task Approve_Should_Return422_When_TheApplicationIsStillADraft()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        var applicationId = Data(await StartAsync(host)).GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, $"{ReviewRoute}/{applicationId}/approval", await AdminTokenAsync(), new { reason = "ok" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(response).Should().Be("HOST_APPLICATION_NOT_UNDER_REVIEW");
    }

    // ----- limpeza -----

    [Fact]
    public async Task Purge_Should_ExpireAbandonedDrafts_When_TheyPassTheLimit()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);
        await UploadAsync(host, "front", TestImages.PngVariant(1));
        var before = await fixture.Bucket.CountPrivateAsync();

        // Rascunho "de 8 dias atrás": a rodada roda com um relógio 8 dias à frente.
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = new PurgeHostApplicationsCommandHandler(
            scope.ServiceProvider.GetRequiredService<Domain.HostApplications.IHostApplicationRepository>(),
            scope.ServiceProvider.GetRequiredService<Domain.Hosts.IHostsUnitOfWork>(),
            scope.ServiceProvider.GetRequiredService<Shared.Contracts.Storage.IPrivateImageStorage>(),
            scope.ServiceProvider.GetRequiredService<Application.Abstractions.IHostVerificationPolicy>(),
            scope.ServiceProvider.GetRequiredService<Shared.Contracts.Audit.IAuditTrail>(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(8)));

        var result = await handler.HandleAsync(new PurgeHostApplicationsCommand(), Ct);

        result.Value!.ExpiredDrafts.Should().Be(1);
        result.Value.PurgedApplications.Should().Be(1);
        (await fixture.Bucket.CountPrivateAsync()).Should().Be(before - 1);
        Data(await SendAsync(HttpMethod.Get, ApplicationRoute, host)).GetProperty("application").GetProperty("status").GetString().Should().Be("expired");
    }

    [Fact]
    public async Task Purge_Should_RunThroughTheValidatedHandler_When_ResolvedFromTheContainer()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PurgeHostApplicationsCommand, PurgeHostApplicationsResponse>>();

        var result = await handler.HandleAsync(new PurgeHostApplicationsCommand(), Ct);

        result.IsSuccess.Should().BeTrue();
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

    private Task<HttpResponseMessage> StartAsync(string token) =>
        SendAsync(HttpMethod.Post, ApplicationRoute, token, new { cpf = Cpf, documentType = "cnh", acceptNotice = true });

    // As três fotos num pedido só, como o app manda.
    private async Task UploadAllAsync(string token) =>
        (await UploadRawAsync(token, ("front", TestImages.PngVariant(1)), ("back", TestImages.PngVariant(2)), ("selfie", TestImages.PngVariant(3))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private async Task<Guid> SubmitCompleteApplicationAsync(string token)
    {
        await StartAsync(token);
        await UploadAllAsync(token);
        var submitted = await SendAsync(HttpMethod.Post, $"{ApplicationRoute}/submission", token);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        return Data(submitted).GetProperty("id").GetGuid();
    }

    private async Task UploadAsync(string token, string kind, byte[] image) =>
        (await UploadRawAsync(token, kind, image)).StatusCode.Should().Be(HttpStatusCode.OK);

    private Task<HttpResponseMessage> UploadRawAsync(string token, string kind, byte[] image) =>
        UploadRawAsync(token, (kind, image));

    /// <summary><c>PUT /documents</c> com uma parte por foto (<c>front</c>, <c>back</c>, <c>selfie</c>).</summary>
    private Task<HttpResponseMessage> UploadRawAsync(string token, params (string Kind, byte[] Image)[] parts)
    {
        var form = new MultipartFormDataContent();
        foreach (var (kind, image) in parts)
        {
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, kind, $"{kind}.png");
        }

        var request = new HttpRequestMessage(HttpMethod.Put, $"{ApplicationRoute}/documents") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request, Ct);
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = HostsApiFixture.AdminEmail, password = HostsApiFixture.AdminPassword, role = "admin" },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(response).GetProperty("accessToken").GetString()!;
    }

    private async Task<List<JsonElement>> AuditAsync(string admin, string query)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/audit-entries" + query, admin);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return [.. Data(response).GetProperty("items").EnumerateArray()];
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
