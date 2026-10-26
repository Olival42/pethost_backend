using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Uploads;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.IntegrationTests.Pets;

/// <summary>
/// Upload direto ao bucket (<c>/api/v1/uploads</c>) ponta a ponta, com a foto do pet: pedir a URL
/// assinada, fazer o <c>PUT</c> direto no RustFS (sem passar pela API) e confirmar. Cobre a
/// conferência do conteúdo real, a idempotência, a posse e a limpeza.
/// </summary>
[Collection(PetsApiCollection.Name)]
public sealed class PetPhotoDirectUploadTests(PetsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string UploadsRoute = "/api/v1/uploads";
    private const string PetsRoute = "/api/v1/pets";

    private HttpClient _client = null!;

    /// <summary>O navegador: fala direto com o bucket, sem token da API.</summary>
    private readonly HttpClient _browser = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        _client = fixture.CreateClient();
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _browser.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task DirectUpload_Should_AddThePhotoOnlyAfterConfirming_When_TheFileWasSentToTheBucket()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);

        var ticket = await RequestAsync(token, PetPhoto(petId, TestImages.Png.Length));
        ticket.Method.Should().Be("PUT");
        ticket.Headers.Should().Equal(new Dictionary<string, string> { ["Content-Type"] = "image/png" });
        ticket.SizeBytes.Should().Be(TestImages.Png.Length);

        (await PutAsync(ticket, TestImages.Png)).StatusCode.Should().Be(HttpStatusCode.OK);

        var staged = Staged(ticket);
        (await fixture.Bucket.ListPrivateKeysAsync()).Should().Contain(staged, "o navegador gravou na área de espera do bucket privado");
        (await _browser.GetAsync(fixture.Bucket.PrivateObjectUrl(staged), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "o arquivo pendente é privado");
        Photos(await GetPetAsync(token, petId)).Should().BeEmpty("a foto só entra na confirmação");

        var confirmed = await ConfirmAsync(token, ticket.UploadId);

        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync(confirmed);
        body.GetProperty("data").GetProperty("status").GetString().Should().Be("completed");
        body.GetProperty("data").GetProperty("purpose").GetString().Should().Be("pet-photo");
        var photos = body.GetProperty("data").GetProperty("result").GetProperty("photos").EnumerateArray().ToList();
        photos.Should().ContainSingle();
        var url = photos[0].GetProperty("url").GetString()!;
        url.Should().StartWith(fixture.Bucket.PublicBaseUrl + "/pets/");
        (await _browser.GetByteArrayAsync(url, Ct)).Should().Equal(TestImages.Png);

        (await fixture.Bucket.ListPrivateKeysAsync()).Should().NotContain(staged, "o arquivo da área de espera sai depois de aplicado");
    }

    [Fact]
    public async Task Confirm_Should_ReturnTheSameResultWithoutDuplicating_When_CalledTwice()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        var ticket = await RequestAsync(token, PetPhoto(petId, TestImages.Png.Length));
        await PutAsync(ticket, TestImages.Png);

        var first = await ReadAsync(await ConfirmAsync(token, ticket.UploadId));
        var second = await ConfirmAsync(token, ticket.UploadId);

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(second)).GetProperty("data").GetProperty("result").GetRawText()
            .Should().Be(first.GetProperty("data").GetProperty("result").GetRawText());
        Photos(await GetPetAsync(token, petId)).Should().ContainSingle("a segunda confirmação não adiciona outra foto");
    }

    [Fact]
    public async Task Request_Should_Return404AndSignNothing_When_ThePetBelongsToAnotherAccount()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(owner);
        var stranger = await RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35");

        var response = await SendAsync(HttpMethod.Post, UploadsRoute, stranger, PetPhoto(petId, TestImages.Png.Length));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("PET_NOT_FOUND");
        (await CountSessionsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Request_Should_Return422_When_ThePetAlreadyHasThreePhotos()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        for (var i = 1; i <= 3; i++)
            await UploadAndConfirmAsync(token, petId, TestImages.PngVariant(i));

        var response = await SendAsync(HttpMethod.Post, UploadsRoute, token, PetPhoto(petId, TestImages.Png.Length));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("PET_PHOTO_LIMIT_REACHED");
    }

    [Fact]
    public async Task Request_Should_Return400ForEachField_When_TheMetadataIsInvalid()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");

        var response = await SendAsync(HttpMethod.Post, UploadsRoute, token, new { purpose = "pet-photo", contentType = "application/pdf", sizeBytes = ImageRules.MaxBytes + 1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Reasons(await ReadAsync(response)).Should().BeEquivalentTo("UPLOAD_CONTENT_TYPE_INVALID", "UPLOAD_SIZE_TOO_LARGE", "UPLOAD_RESOURCE_ID_REQUIRED");
    }

    [Fact]
    public async Task Request_Should_Return403_When_ThePurposeIsNotForTheRole()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");

        var response = await SendAsync(HttpMethod.Post, UploadsRoute, token, new { purpose = "listing-photo", contentType = "image/png", sizeBytes = 100, resourceId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(response)).Should().Be("UPLOAD_PURPOSE_FORBIDDEN");
    }

    [Fact]
    public async Task Put_Should_BeRefusedByTheBucket_When_TheSizeOrTheContentTypeDifferFromTheSignedOnes()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        var ticket = await RequestAsync(token, PetPhoto(petId, TestImages.Png.Length));

        var bigger = new byte[TestImages.Png.Length + 1000];
        TestImages.Png.CopyTo(bigger, 0);
        (await PutAsync(ticket, bigger)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "o tamanho vai na assinatura");
        (await PutAsync(ticket, TestImages.Png, "image/jpeg")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "o content-type vai na assinatura");
    }

    [Fact]
    public async Task Confirm_Should_Return422AndKeepThePending_When_TheFileWasNotSentYet()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        var ticket = await RequestAsync(token, PetPhoto(petId, TestImages.Png.Length));

        var early = await ConfirmAsync(token, ticket.UploadId);

        early.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(early)).Should().Be("UPLOAD_FILE_NOT_RECEIVED");

        await PutAsync(ticket, TestImages.Png);
        (await ConfirmAsync(token, ticket.UploadId)).StatusCode.Should().Be(HttpStatusCode.OK, "dá para enviar e confirmar de novo");
    }

    [Fact]
    public async Task Confirm_Should_Return404_When_TheUploadIsFromAnotherAccount()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(owner);
        var ticket = await RequestAsync(owner, PetPhoto(petId, TestImages.Png.Length));
        await PutAsync(ticket, TestImages.Png);
        var stranger = await RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35");

        var response = await ConfirmAsync(stranger, ticket.UploadId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("UPLOAD_NOT_FOUND");
        Photos(await GetPetAsync(owner, petId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_Should_RejectAndDeleteTheFile_When_TheContentIsNotAnImage()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        var ticket = await RequestAsync(token, PetPhoto(petId, TestImages.NotAnImage.Length));
        await PutAsync(ticket, TestImages.NotAnImage);
        var publicBefore = await fixture.Bucket.CountAsync();

        var response = await ConfirmAsync(token, ticket.UploadId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "o content-type declarado não basta: vale o conteúdo");
        Reasons(await ReadAsync(response)).Should().Equal("IMAGE_UNSUPPORTED_FORMAT");
        (await fixture.Bucket.ListPrivateKeysAsync()).Should().NotContain(Staged(ticket));
        (await fixture.Bucket.CountAsync()).Should().Be(publicBefore);

        var again = await ConfirmAsync(token, ticket.UploadId);
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(again)).Should().Be("UPLOAD_ALREADY_REJECTED");
    }

    [Fact]
    public async Task Confirm_Should_ReplaceThePhotoAndDeleteTheOldImage_When_APhotoIdIsGiven()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        await UploadAndConfirmAsync(token, petId, TestImages.PngVariant(1));
        var old = Photos(await GetPetAsync(token, petId)).Single();

        var image = TestImages.PngVariant(2);
        var ticket = await RequestAsync(token, new { purpose = "pet-photo", contentType = "image/png", sizeBytes = image.Length, resourceId = petId, photoId = old.Id });
        await PutAsync(ticket, image);
        (await fixture.Bucket.ExistsAsync(old.Url)).Should().BeTrue("a foto anterior só sai depois que a nova foi gravada");

        (await ConfirmAsync(token, ticket.UploadId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var current = Photos(await GetPetAsync(token, petId)).Single();
        current.Id.Should().Be(old.Id);
        current.Url.Should().NotBe(old.Url);
        (await fixture.Bucket.ExistsAsync(old.Url)).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_Should_Return422AndDeleteTheFile_When_TheUploadExpired()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        var ticket = await RequestAsync(token, PetPhoto(petId, TestImages.Png.Length));
        await PutAsync(ticket, TestImages.Png);
        await ExpireAsync(ticket.UploadId);

        var response = await ConfirmAsync(token, ticket.UploadId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("UPLOAD_EXPIRED");
        (await fixture.Bucket.ListPrivateKeysAsync()).Should().NotContain(Staged(ticket));
        Photos(await GetPetAsync(token, petId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cleanup_Should_ExpireAbandonedUploadsAndDeleteTheirFiles_When_TheConfirmWindowPassed()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        var ticket = await RequestAsync(token, PetPhoto(petId, TestImages.Png.Length));
        await PutAsync(ticket, TestImages.Png);
        await ExpireAsync(ticket.UploadId);

        await fixture.Services.GetRequiredService<UploadCleanupService>().RunOnceAsync(Ct);

        (await StatusAsync(ticket.UploadId)).Should().Be("expired");
        (await fixture.Bucket.ListPrivateKeysAsync()).Should().NotContain(Staged(ticket));
    }

    // ----- apoio -----

    private sealed record Ticket(Guid UploadId, string UploadUrl, string Method, Dictionary<string, string> Headers, long SizeBytes, long ExpiresAt, long ConfirmBy);

    private sealed record Photo(Guid Id, string Url);

    /// <summary>Chave do arquivo na área de espera (gerada pela API, nunca pelo cliente).</summary>
    private static string Staged(Ticket ticket) => $"uploads/pending/{ticket.UploadId:N}";

    private static object PetPhoto(Guid petId, long size) =>
        new { purpose = "pet-photo", contentType = "image/png", sizeBytes = size, resourceId = petId };

    private async Task<Ticket> RequestAsync(string token, object body)
    {
        var response = await SendAsync(HttpMethod.Post, UploadsRoute, token, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));

        return (await ReadAsync(response)).GetProperty("data").Deserialize<Ticket>(Json)!;
    }

    /// <summary>O <c>PUT</c> do navegador: direto no bucket, com só o <c>Content-Type</c> (o tamanho vai sozinho).</summary>
    private Task<HttpResponseMessage> PutAsync(Ticket ticket, byte[] file, string? contentType = null)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? ticket.Headers["Content-Type"]);

        return _browser.PutAsync(ticket.UploadUrl, content, Ct);
    }

    private Task<HttpResponseMessage> ConfirmAsync(string token, Guid uploadId) =>
        SendAsync(HttpMethod.Post, $"{UploadsRoute}/{uploadId}/confirm", token);

    private async Task UploadAndConfirmAsync(string token, Guid petId, byte[] image)
    {
        var ticket = await RequestAsync(token, PetPhoto(petId, image.Length));
        (await PutAsync(ticket, image)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ConfirmAsync(token, ticket.UploadId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task ExpireAsync(Guid uploadId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "UPDATE uploads.upload_sessions SET confirm_expires_at = now() - interval '1 minute' WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", uploadId);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<string?> StatusAsync(Guid uploadId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT status FROM uploads.upload_sessions WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", uploadId);
        return (string?)await command.ExecuteScalarAsync(Ct);
    }

    private async Task<long> CountSessionsAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM uploads.upload_sessions", connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<string> RegisterOwnerAsync(string email, string cpf)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/owners/register", new
        {
            fullName = "Camila Souza",
            email,
            password = "Nova@Senha123",
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf,
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> CreatePetAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, PetsRoute, token, new
        {
            species = "dog",
            name = "Pipoca",
            size = "medium",
            sex = "female",
            isNeutered = true,
            isVaccinated = true,
            goodWithDogs = true,
            goodWithCats = false,
            goodWithKids = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GetPetAsync(string token, Guid petId) =>
        await ReadAsync(await SendAsync(HttpMethod.Get, $"{PetsRoute}/{petId}", token));

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

    private static List<Photo> Photos(JsonElement body) =>
        [.. body.GetProperty("data").GetProperty("photos").EnumerateArray()
            .Select(p => new Photo(p.GetProperty("id").GetGuid(), p.GetProperty("url").GetString()!))];

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    private static List<string?> Reasons(JsonElement body) =>
        [.. body.GetProperty("error").GetProperty("details").EnumerateArray()
            .SelectMany(d => d.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()))];
}
