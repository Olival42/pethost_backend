using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Owners.IntegrationTests.Owners;

/// <summary>
/// Foto de perfil do tutor ponta a ponta: a API recebe o arquivo, grava no bucket (RustFS em
/// container), guarda a URL na conta e apaga a anterior.
/// </summary>
[Collection(OwnersApiCollection.Name)]
public sealed class OwnerAvatarTests(OwnersApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string AvatarRoute = "/api/v1/owners/me/avatar";

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

    [Fact]
    public async Task ChangeAvatar_Should_StoreTheImageAndSaveItsPublicUrl_When_ImageIsValid()
    {
        var token = await RegisterOwnerAsync();

        var response = await UploadAsync(token, TestImages.Png, "eu.png");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var url = AvatarUrl(await ReadAsync(response));
        url.Should().StartWith(fixture.Bucket.PublicBaseUrl + "/avatars/").And.EndWith(".png");
        (await fixture.Bucket.ExistsAsync(url!)).Should().BeTrue();

        // O link é público: o front mostra direto, sem token.
        using var anonymous = new HttpClient();
        var image = await anonymous.GetAsync(url, Ct);
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

        var me = await SendAsync(HttpMethod.Get, "/api/v1/owners/me", token);
        AvatarUrl(await ReadAsync(me)).Should().Be(url);
    }

    [Fact]
    public async Task ChangeAvatar_Should_DeleteThePreviousImage_When_AvatarIsReplaced()
    {
        var token = await RegisterOwnerAsync();
        var first = AvatarUrl(await ReadAsync(await UploadAsync(token, TestImages.Png, "a.png")));

        var second = AvatarUrl(await ReadAsync(await UploadAsync(token, TestImages.Jpeg, "b.jpg")));

        second.Should().NotBe(first).And.EndWith(".jpg");
        (await fixture.Bucket.ExistsAsync(second!)).Should().BeTrue();
        (await fixture.Bucket.ExistsAsync(first!)).Should().BeFalse("a foto anterior sai do bucket");
    }

    [Fact]
    public async Task ChangeAvatar_Should_Return400OnFile_When_ContentIsNotAnImage()
    {
        var token = await RegisterOwnerAsync();
        var before = await fixture.Bucket.CountAsync();

        // Nome e content-type dizem PNG; o conteúdo é texto.
        var response = await UploadAsync(token, TestImages.NotAnImage, "foto.png");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadAsync(response);
        Fields(body).Should().Equal("file");
        (await fixture.Bucket.CountAsync()).Should().Be(before, "nada vai para o bucket");
    }

    [Fact]
    public async Task ChangeAvatar_Should_Return400_When_NoFileIsSent()
    {
        var token = await RegisterOwnerAsync();
        var request = new HttpRequestMessage(HttpMethod.Put, AvatarRoute) { Content = new MultipartFormDataContent() };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("file");
    }

    [Fact]
    public async Task ChangeAvatar_Should_Return415SayingMultipart_When_ImageIsSentAsRawBody()
    {
        // O engano comum: mandar a imagem crua (Content-Type: image/jpeg) em vez de multipart.
        var token = await RegisterOwnerAsync();
        var body = new ByteArrayContent(TestImages.Jpeg);
        body.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var request = new HttpRequestMessage(HttpMethod.Put, AvatarRoute) { Content = body };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var error = (await ReadAsync(response)).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("UNSUPPORTED_MEDIA_TYPE");
        error.GetProperty("message").GetString().Should()
            .Be("Unsupported content type. Send multipart/form-data, with the image in the 'file' field.");
    }

    [Fact]
    public async Task RemoveAvatar_Should_ClearTheUrlAndDeleteTheImage_When_OwnerHasAvatar()
    {
        var token = await RegisterOwnerAsync();
        var url = AvatarUrl(await ReadAsync(await UploadAsync(token, TestImages.Png, "eu.png")));

        var response = await SendAsync(HttpMethod.Delete, AvatarRoute, token);
        var again = await SendAsync(HttpMethod.Delete, AvatarRoute, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AvatarUrl(await ReadAsync(response)).Should().BeNull();
        (await fixture.Bucket.ExistsAsync(url!)).Should().BeFalse();
        again.StatusCode.Should().Be(HttpStatusCode.OK, "tirar de novo é idempotente");
    }

    [Fact]
    public async Task UpdateMe_Should_IgnoreAvatarUrl_When_SentInTheJsonBody()
    {
        // A foto só entra por upload: URL digitada no PATCH não é aceita.
        var token = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/owners/me", token, new { fullName = "Camila S.", avatarUrl = "https://evil.example/x.png" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AvatarUrl(await ReadAsync(response)).Should().BeNull();
    }

    // ----- helpers -----

    private async Task<string> RegisterOwnerAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/owners/register", new
        {
            fullName = "Camila Souza",
            email = "camila@exemplo.com",
            password = "Nova@Senha123",
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf = "529.982.247-25",
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> UploadAsync(string token, byte[] image, string fileName)
    {
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var request = new HttpRequestMessage(HttpMethod.Put, AvatarRoute)
        {
            Content = new MultipartFormDataContent { { file, "file", fileName } },
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

    private static string? AvatarUrl(JsonElement body) =>
        body.GetProperty("data").GetProperty("user").TryGetProperty("avatarUrl", out var url) ? url.GetString() : null;

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    private static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];
}
