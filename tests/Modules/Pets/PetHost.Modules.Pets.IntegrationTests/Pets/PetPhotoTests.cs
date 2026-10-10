using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetHost.Modules.Pets.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Storage;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.IntegrationTests.Pets;

/// <summary>
/// Fotos do pet ponta a ponta (até 3): a API recebe o arquivo, grava no bucket (RustFS em
/// container) e na tabela <c>pet.pet_photos</c>. Adicionar (POST), substituir (PATCH) e tirar
/// (DELETE); só o dono mexe.
/// </summary>
[Collection(PetsApiCollection.Name)]
public sealed class PetPhotoTests(PetsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string PetsRoute = "/api/v1/pets";

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

    // ----- adicionar -----

    [Fact]
    public async Task AddPhoto_Should_StoreTheImageAndListIt_When_OwnerSendsAnImage()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);

        var response = await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.Png);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var photos = Photos(await ReadAsync(response));
        photos.Should().ContainSingle();
        photos[0].Url.Should().StartWith(fixture.Bucket.PublicBaseUrl + "/pets/").And.EndWith(".png");

        using var anonymous = new HttpClient();
        var image = await anonymous.GetAsync(photos[0].Url, Ct);
        image.StatusCode.Should().Be(HttpStatusCode.OK, "o link é público");
        (await image.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(TestImages.Png);

        var pet = await SendAsync(HttpMethod.Get, $"{PetsRoute}/{petId}", token);
        Photos(await ReadAsync(pet)).Should().Equal(photos);
    }

    [Fact]
    public async Task AddPhoto_Should_Return422AndStoreNothing_When_PetAlreadyHasThreePhotos()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        for (var i = 1; i <= 3; i++)
            (await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.PngVariant(i))).StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await fixture.Bucket.CountAsync();

        var response = await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.PngVariant(4));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("PET_PHOTO_LIMIT_REACHED");
        (await fixture.Bucket.CountAsync()).Should().Be(before, "a quarta foto nem chega ao bucket");
    }

    [Fact]
    public async Task AddPhoto_Should_Return404AndStoreNothing_When_PetBelongsToAnotherAccount()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(owner);
        var other = await RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35");
        var before = await fixture.Bucket.CountAsync();

        var response = await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", other, TestImages.Png);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("PET_NOT_FOUND");
        (await fixture.Bucket.CountAsync()).Should().Be(before, "quem não é dono não chega a enviar nada");
    }

    [Fact]
    public async Task AddPhoto_Should_Return400OnFile_When_ImageIsTooLarge()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        var tooLarge = new byte[ImageRules.MaxBytes + 1];
        TestImages.Png.CopyTo(tooLarge, 0);

        var response = await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, tooLarge);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadAsync(response);
        Fields(body).Should().Equal("file");
        Messages(body).Should().Equal("The image must be at most 5 MB.");
    }

    [Fact]
    public async Task AddPhoto_Should_Return413InTheEnvelope_When_BodyIsOverTheLimit()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);

        var response = await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, new byte[ImageRules.MaxRequestBytes + 1]);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        Code(await ReadAsync(response)).Should().Be("PAYLOAD_TOO_LARGE");
    }

    [Fact]
    public async Task AddPhoto_Should_Return400OnFile_When_ContentIsNotAnImage()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);

        var response = await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.NotAnImage);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("file");
    }

    [Fact]
    public async Task AddPhoto_Should_Return409AndLeaveNoFileBehind_When_ThePetAlreadyHasThatImage()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.Png);
        var before = await fixture.Bucket.CountAsync();

        var response = await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.Png);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("PET_PHOTO_ALREADY_EXISTS");
        (await fixture.Bucket.CountAsync()).Should().Be(before, "a cópia enviada é apagada do bucket");
    }

    [Fact]
    public async Task AddPhoto_Should_AcceptTheSameImage_When_ItIsForAnotherPet()
    {
        // A regra é por pet: a mesma foto pode estar em dois pets diferentes.
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var pipoca = await CreatePetAsync(token);
        var mia = await CreatePetAsync(token);
        await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{pipoca}/photos", token, TestImages.Png);

        var response = await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{mia}/photos", token, TestImages.Png);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----- substituir -----

    [Fact]
    public async Task ReplacePhoto_Should_KeepTheIdAndPositionAndDeleteTheOldImage_When_PhotoIsFromThePet()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.PngVariant(1));
        var before = Photos(await ReadAsync(await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.PngVariant(2))));
        var second = before[1];

        var response = await SendFileAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}/photos/{second.Id}", token, TestImages.Jpeg);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = Photos(await ReadAsync(response));
        after.Select(p => p.Id).Should().Equal(before.Select(p => p.Id), "a foto mantém o id e a posição");
        after[0].Should().Be(before[0], "a outra foto não muda");
        after[1].Url.Should().NotBe(second.Url).And.EndWith(".jpg");
        (await fixture.Bucket.ExistsAsync(after[1].Url)).Should().BeTrue();
        (await fixture.Bucket.ExistsAsync(second.Url)).Should().BeFalse("a imagem antiga sai do bucket");
    }

    [Fact]
    public async Task ReplacePhoto_Should_Return404AndStoreNothing_When_PhotoIsNotFromThePet()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        var before = await fixture.Bucket.CountAsync();

        var response = await SendFileAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}/photos/{Guid.CreateVersion7()}", token, TestImages.Png);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("PET_PHOTO_NOT_FOUND");
        (await fixture.Bucket.CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task ReplacePhoto_Should_Return409_When_TheNewImageIsAlreadyInAnotherPhoto()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.PngVariant(1));
        var photos = Photos(await ReadAsync(await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.PngVariant(2))));

        var response = await SendFileAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}/photos/{photos[1].Id}", token, TestImages.PngVariant(1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("PET_PHOTO_ALREADY_EXISTS");
        (await fixture.Bucket.ExistsAsync(photos[1].Url)).Should().BeTrue("a foto continua a mesma");
    }

    // ----- tirar -----

    [Fact]
    public async Task RemovePhoto_Should_DeleteOnlyThatPhoto_When_PhotoIsFromThePet()
    {
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.PngVariant(1));
        var photos = Photos(await ReadAsync(await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.PngVariant(2))));

        var response = await SendAsync(HttpMethod.Delete, $"{PetsRoute}/{petId}/photos/{photos[0].Id}", token);
        var again = await SendAsync(HttpMethod.Delete, $"{PetsRoute}/{petId}/photos/{photos[0].Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Photos(await ReadAsync(response)).Should().Equal(photos[1]);
        (await fixture.Bucket.ExistsAsync(photos[0].Url)).Should().BeFalse();
        (await fixture.Bucket.ExistsAsync(photos[1].Url)).Should().BeTrue();
        again.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foto já não existe");
        Code(await ReadAsync(again)).Should().Be("PET_PHOTO_NOT_FOUND");
    }

    // ----- JSON e banco -----

    [Fact]
    public async Task Register_Should_IgnorePhotoUrl_When_SentInTheJsonBody()
    {
        // As fotos só entram por upload: URL digitada no cadastro não é aceita.
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");

        var response = await SendAsync(HttpMethod.Post, PetsRoute, token, Pet(photoUrl: "https://evil.example/x.png"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        Photos(await ReadAsync(response)).Should().BeEmpty();
    }

    [Fact]
    public async Task Database_Should_RejectAFourthPhoto_When_InsertedDirectly()
    {
        // O limite também vale no banco: vaga de 1 a 3, única por pet.
        var token = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25");
        var petId = await CreatePetAsync(token);
        await SendFileAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/photos", token, TestImages.Png);

        await using var scope = fixture.Services.CreateAsyncScope();
        var pets = scope.ServiceProvider.GetRequiredService<PetsDbContext>();

        var sameSlot = () => pets.Database.ExecuteSqlAsync(
            $"INSERT INTO pet.pet_photos (id, pet_id, url, position, created_at) VALUES ({Guid.CreateVersion7()}, {petId}, 'https://x.test/a.png', 1, now())",
            Ct);
        var fourthSlot = () => pets.Database.ExecuteSqlAsync(
            $"INSERT INTO pet.pet_photos (id, pet_id, url, position, created_at) VALUES ({Guid.CreateVersion7()}, {petId}, 'https://x.test/a.png', 4, now())",
            Ct);

        (await sameSlot.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("uq_pet_photos_pet_id_position");
        (await fourthSlot.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_pet_photos_position");
    }

    // ----- helpers -----

    private sealed record Photo(Guid Id, string Url);

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
        var response = await SendAsync(HttpMethod.Post, PetsRoute, token, Pet());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("id").GetGuid();
    }

    private static object Pet(string? photoUrl = null) =>
        new
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
            photoUrl,
        };

    private Task<HttpResponseMessage> SendFileAsync(HttpMethod method, string route, string token, byte[] image)
    {
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var request = new HttpRequestMessage(method, route)
        {
            Content = new MultipartFormDataContent { { file, "file", "pipoca.png" } },
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

    private static List<Photo> Photos(JsonElement body) =>
        [.. body.GetProperty("data").GetProperty("photos").EnumerateArray()
            .Select(p => new Photo(p.GetProperty("id").GetGuid(), p.GetProperty("url").GetString()!))];

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    private static List<string?> Fields(JsonElement body) =>
        [.. body.GetProperty("error").GetProperty("details").EnumerateArray().Select(d => d.GetProperty("field").GetString())];

    private static List<string?> Messages(JsonElement body) =>
        [.. body.GetProperty("error").GetProperty("details").EnumerateArray()
            .SelectMany(d => d.GetProperty("messages").EnumerateArray().Select(m => m.GetString()))];
}
