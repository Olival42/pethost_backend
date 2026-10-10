using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Pets.Infrastructure.Persistence;
using Xunit;

namespace PetHost.Modules.Pets.IntegrationTests.Pets;

/// <summary>
/// Pets ponta a ponta pela API: cadastro do tutor e do anfitrião, PATCH, desativar/reativar,
/// as listagens e quem vê o quê. O anfitrião ainda não tem rota de cadastro: o perfil é
/// gravado direto na tabela <c>host.hosts</c>.
/// </summary>
[Collection(PetsApiCollection.Name)]
public sealed class PetEndpointsTests(PetsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string PetsRoute = "/api/v1/pets";
    private const string Password = "Nova@Senha123";
    private const string Microchip = "985 112 004 567 890";

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

    // ----- cadastro -----

    [Fact]
    public async Task Register_Should_Return201WithThePetOfTheOwner_When_OwnerSendsAValidPet()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Post, PetsRoute, owner.Token, Pipoca(microchip: Microchip));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var pet = (await ReadAsync(response)).GetProperty("data");
        response.Headers.Location!.ToString().Should().Be($"{PetsRoute}/{pet.GetProperty("id").GetGuid()}");

        pet.GetProperty("ownerId").GetGuid().Should().Be(owner.OwnerId, "o pet guarda o id do tutor, não o da conta");
        pet.TryGetProperty("hostId", out _).Should().BeFalse("campo nulo é omitido");
        pet.GetProperty("keeper").GetProperty("id").GetGuid().Should().Be(owner.OwnerId);
        pet.GetProperty("keeper").GetProperty("type").GetString().Should().Be("owner");
        pet.GetProperty("keeper").GetProperty("name").GetString().Should().Be("Camila Souza");
        pet.GetProperty("species").GetString().Should().Be("dog");
        pet.GetProperty("size").GetString().Should().Be("medium");
        pet.GetProperty("sex").GetString().Should().Be("female");
        pet.GetProperty("birthDate").GetString().Should().Be("2022-03-15");
        pet.GetProperty("goodWithCats").GetBoolean().Should().BeFalse();
        pet.GetProperty("isActive").GetBoolean().Should().BeTrue();
        pet.GetProperty("weightKg").GetDecimal().Should().Be(14.5m);
        pet.GetProperty("microchip").GetString().Should().Be("985112004567890");
        pet.GetProperty("allergies").GetString().Should().Be("Frango");
    }

    [Fact]
    public async Task Update_Should_ChangeWeightAndClearMicrochip_When_Sent()
    {
        var owner = await RegisterOwnerAsync();
        var petId = await CreatePetAsync(owner.Token, Pipoca(microchip: Microchip));

        var response = await SendAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}", owner.Token, new { weightKg = 0.035, microchip = "" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pet = (await ReadAsync(response)).GetProperty("data");
        pet.GetProperty("weightKg").GetDecimal().Should().Be(0.035m);
        pet.TryGetProperty("microchip", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Register_Should_Return400WithEveryRequiredField_When_BodyIsEmpty()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Post, PetsRoute, owner.Token, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadAsync(response);
        Code(body).Should().Be("VALIDATION_ERROR");
        Fields(body).Should().BeEquivalentTo(
            ["species", "name", "sex", "isNeutered", "isVaccinated", "goodWithDogs", "goodWithCats", "goodWithKids"]);
    }

    [Fact]
    public async Task Register_Should_Return400_When_DogHasNoSizeAndExoticHasNoDescription()
    {
        var owner = await RegisterOwnerAsync();

        var dog = await SendAsync(HttpMethod.Post, PetsRoute, owner.Token, Pipoca(size: null));
        var exotic = await SendAsync(HttpMethod.Post, PetsRoute, owner.Token, Pipoca(species: "exotic", size: null));

        Fields(await ReadAsync(dog)).Should().Equal("size");
        Fields(await ReadAsync(exotic)).Should().Equal("speciesDescription");
    }

    [Fact]
    public async Task Register_Should_Return201_When_ExoticComesWithDescription()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Post, PetsRoute, owner.Token, Pipoca(species: "exotic", size: null, speciesDescription: "Iguana verde"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync(response)).GetProperty("data").GetProperty("speciesDescription").GetString().Should().Be("Iguana verde");
    }

    [Fact]
    public async Task Register_Should_Return404KeeperNotFound_When_HostAccountHasNoHostProfile()
    {
        // O cadastro de anfitrião (com CPF/CNPJ) ainda não existe: a conta sozinha não basta.
        var host = await RegisterHostAccountAsync();

        var response = await SendAsync(HttpMethod.Post, PetsRoute, host.Token, Pipoca());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("PET_KEEPER_NOT_FOUND");
    }

    [Fact]
    public async Task Register_Should_StoreThePetOfTheHost_When_HostHasAProfile()
    {
        var host = await RegisterHostAsync();

        var response = await SendAsync(HttpMethod.Post, PetsRoute, host.Token, Pipoca(name: "Thor"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var pet = (await ReadAsync(response)).GetProperty("data");
        pet.GetProperty("hostId").GetGuid().Should().Be(host.HostId);
        pet.TryGetProperty("ownerId", out _).Should().BeFalse();
        pet.GetProperty("keeper").GetProperty("name").GetString().Should().Be("Dona Cida");
    }

    [Fact]
    public async Task Register_Should_Return401_When_ThereIsNoToken()
    {
        var response = await _client.PostAsJsonAsync(PetsRoute, Pipoca(), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_Should_Return403_When_AccountIsAdmin()
    {
        var response = await SendAsync(HttpMethod.Post, PetsRoute, await AdminTokenAsync(), Pipoca());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- PATCH -----

    [Fact]
    public async Task Update_Should_ChangeOnlyTheSentFields_When_PatchIsPartial()
    {
        var owner = await RegisterOwnerAsync();
        var petId = await CreatePetAsync(owner.Token);

        var response = await SendAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}", owner.Token, new { name = "Paçoca", breed = "", goodWithCats = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pet = (await ReadAsync(response)).GetProperty("data");
        pet.GetProperty("name").GetString().Should().Be("Paçoca");
        pet.TryGetProperty("breed", out _).Should().BeFalse("texto vazio limpa o campo");
        pet.GetProperty("goodWithCats").GetBoolean().Should().BeTrue();
        pet.GetProperty("size").GetString().Should().Be("medium", "porte não veio, não muda");
        pet.GetProperty("notes").GetString().Should().Be("Morre de medo de fogos.");
    }

    [Fact]
    public async Task Update_Should_KeepTheSizeForACatAndDropItForARabbit_When_SpeciesChanges()
    {
        var owner = await RegisterOwnerAsync();
        var petId = await CreatePetAsync(owner.Token);

        var cat = await SendAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}", owner.Token, new { species = "cat" });
        var rabbit = await SendAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}", owner.Token, new { species = "rabbit" });

        cat.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(cat)).GetProperty("data").GetProperty("size").GetString().Should().Be("medium");
        rabbit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(rabbit)).GetProperty("data").TryGetProperty("size", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Register_Should_AcceptACatWithAndWithoutSize_When_Sent()
    {
        var owner = await RegisterOwnerAsync();

        var withSize = await SendAsync(HttpMethod.Post, PetsRoute, owner.Token, Pipoca(species: "cat", size: "small", name: "Mia"));
        var withoutSize = await SendAsync(HttpMethod.Post, PetsRoute, owner.Token, Pipoca(species: "cat", size: null, name: "Tom"));
        var rabbit = await SendAsync(HttpMethod.Post, PetsRoute, owner.Token, Pipoca(species: "rabbit", size: "small", name: "Nina"));

        withSize.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync(withSize)).GetProperty("data").GetProperty("size").GetString().Should().Be("small");
        withoutSize.StatusCode.Should().Be(HttpStatusCode.Created);
        rabbit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(rabbit)).Should().Equal("size");
    }

    [Fact]
    public async Task Update_Should_Return400WithEveryField_When_ResultIsInvalid()
    {
        var owner = await RegisterOwnerAsync();
        var petId = await CreatePetAsync(owner.Token);

        var response = await SendAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}", owner.Token, new
        {
            name = "",
            size = "giant",
            birthDate = "2099-01-01",
            breed = new string('x', 61),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["name", "size", "birthDate", "breed"]);
    }

    [Fact]
    public async Task Update_Should_Return404_When_PetBelongsToAnotherOwner()
    {
        var owner = await RegisterOwnerAsync();
        var petId = await CreatePetAsync(owner.Token);
        var other = await RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35");

        var response = await SendAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}", other.Token, new { name = "Roubada" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "quem não é dono não descobre que o pet existe");
        Code(await ReadAsync(response)).Should().Be("PET_NOT_FOUND");
    }

    [Fact]
    public async Task Update_Should_Return404_When_PetDoesNotExist()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Patch, $"{PetsRoute}/{Guid.CreateVersion7()}", owner.Token, new { name = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("PET_NOT_FOUND");
    }

    // ----- desativar / reativar -----

    [Fact]
    public async Task Deactivation_Should_DeactivateAndReactivateThePet_When_OwnerAsks()
    {
        var owner = await RegisterOwnerAsync();
        var petId = await CreatePetAsync(owner.Token);

        var deactivated = await SendAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/deactivation", owner.Token);
        var again = await SendAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/deactivation", owner.Token);
        var reactivated = await SendAsync(HttpMethod.Delete, $"{PetsRoute}/{petId}/deactivation", owner.Token);

        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        var off = (await ReadAsync(deactivated)).GetProperty("data");
        off.GetProperty("isActive").GetBoolean().Should().BeFalse();
        off.GetProperty("deactivatedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        again.StatusCode.Should().Be(HttpStatusCode.OK, "desativar de novo é idempotente");

        reactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        var on = (await ReadAsync(reactivated)).GetProperty("data");
        on.GetProperty("isActive").GetBoolean().Should().BeTrue();
        on.TryGetProperty("deactivatedAt", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Deactivation_Should_Return404_When_PetBelongsToAnotherAccount()
    {
        var host = await RegisterHostAsync();
        var petId = await CreatePetAsync(host.Token);
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/deactivation", owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("PET_NOT_FOUND");
    }

    [Fact]
    public async Task Update_Should_ChangeTheProfile_When_PetIsInactive()
    {
        var owner = await RegisterOwnerAsync();
        var petId = await CreatePetAsync(owner.Token);
        await SendAsync(HttpMethod.Post, $"{PetsRoute}/{petId}/deactivation", owner.Token);

        var response = await SendAsync(HttpMethod.Patch, $"{PetsRoute}/{petId}", owner.Token, new { name = "Paçoca" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pet = (await ReadAsync(response)).GetProperty("data");
        pet.GetProperty("name").GetString().Should().Be("Paçoca");
        pet.GetProperty("isActive").GetBoolean().Should().BeFalse("editar não reativa");
    }

    // ----- erros sem corpo do ASP.NET Core vão no envelope -----

    [Theory]
    [InlineData("/api/v1/pets/nao-e-um-guid")]
    [InlineData("/api/v1/rota-que-nao-existe")]
    public async Task UnknownRoute_Should_Return404InTheEnvelope_When_RouteDoesNotMatch(string route)
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Get, route, owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await ReadAsync(response);
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        Code(body).Should().Be("NOT_FOUND");
        body.GetProperty("error").GetProperty("message").GetString().Should().Be("The requested resource was not found.");
        body.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task WrongMethod_Should_Return405InTheEnvelope_When_RouteExistsWithAnotherMethod()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Put, $"{PetsRoute}/me", owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        Code(await ReadAsync(response)).Should().Be("METHOD_NOT_ALLOWED");
    }

    // ----- listagens e visibilidade -----

    [Fact]
    public async Task ListMine_Should_ReturnActiveAndInactivePetsOfTheLoggedOwnerOnly_When_Called()
    {
        var owner = await RegisterOwnerAsync();
        var pipoca = await CreatePetAsync(owner.Token);
        var mia = await CreatePetAsync(owner.Token, Pipoca(species: "cat", size: null, name: "Mia"));
        await SendAsync(HttpMethod.Post, $"{PetsRoute}/{mia}/deactivation", owner.Token);

        var other = await RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35");
        await CreatePetAsync(other.Token, Pipoca(name: "Rex"));

        var response = await SendAsync(HttpMethod.Get, $"{PetsRoute}/me", owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ReadAsync(response)).GetProperty("data");
        data.GetProperty("keeper").GetProperty("id").GetGuid().Should().Be(owner.OwnerId, "o dono vem uma vez, no topo");
        data.GetProperty("keeper").GetProperty("name").GetString().Should().Be("Camila Souza");
        var pets = data.GetProperty("pets").EnumerateArray().ToList();
        pets.Select(p => p.GetProperty("id").GetGuid()).Should().BeEquivalentTo([pipoca, mia]);
        pets.SelectMany(p => p.EnumerateObject().Select(f => f.Name))
            .Should().NotContain(["keeper", "ownerId", "hostId"], "o dono vem uma vez, no topo");
    }

    [Fact]
    public async Task GetById_Should_HideTheOwnersPet_When_AnotherOwnerAsks()
    {
        var owner = await RegisterOwnerAsync();
        var petId = await CreatePetAsync(owner.Token);
        var other = await RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35");

        var mine = await SendAsync(HttpMethod.Get, $"{PetsRoute}/{petId}", owner.Token);
        var theirs = await SendAsync(HttpMethod.Get, $"{PetsRoute}/{petId}", other.Token);
        var admin = await SendAsync(HttpMethod.Get, $"{PetsRoute}/{petId}", await AdminTokenAsync());

        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        theirs.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(theirs)).Should().Be("PET_NOT_FOUND");
        admin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HostPets_Should_ShowTheActivePetsOfTheHouseToAnOwner_When_OwnerLooksAtAHost()
    {
        // O tutor vê com quais animais o pet dele vai conviver na casa do anfitrião.
        var host = await RegisterHostAsync();
        var thor = await CreatePetAsync(host.Token, Pipoca(name: "Thor", size: "large"));
        var old = await CreatePetAsync(host.Token, Pipoca(species: "cat", size: null, name: "Félix"));
        await SendAsync(HttpMethod.Post, $"{PetsRoute}/{old}/deactivation", host.Token);
        var owner = await RegisterOwnerAsync();

        var list = await SendAsync(HttpMethod.Get, $"/api/v1/hosts/{host.HostId}/pets", owner.Token);
        var one = await SendAsync(HttpMethod.Get, $"{PetsRoute}/{thor}", owner.Token);
        var inactive = await SendAsync(HttpMethod.Get, $"{PetsRoute}/{old}", owner.Token);

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ReadAsync(list)).GetProperty("data");
        data.GetProperty("keeper").GetProperty("id").GetGuid().Should().Be(host.HostId);
        data.GetProperty("keeper").GetProperty("name").GetString().Should().Be("Dona Cida");
        var pets = data.GetProperty("pets").EnumerateArray().ToList();
        pets.Should().ContainSingle();
        pets[0].GetProperty("name").GetString().Should().Be("Thor");
        pets[0].GetProperty("size").GetString().Should().Be("large");
        pets[0].TryGetProperty("keeper", out _).Should().BeFalse();

        one.StatusCode.Should().Be(HttpStatusCode.OK);
        inactive.StatusCode.Should().Be(HttpStatusCode.NotFound, "pet inativo do anfitrião só o próprio anfitrião vê");
    }

    [Fact]
    public async Task HostPets_Should_Return404_When_HostDoesNotExist()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/hosts/{Guid.CreateVersion7()}/pets", owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("PET_HOST_NOT_FOUND");
    }

    // ----- banco -----

    [Fact]
    public async Task Database_Should_RejectAPetWithTwoKeepers_When_InsertedDirectly()
    {
        // A regra "exatamente um dono" também vale no banco (CHECK), não só no domínio.
        var owner = await RegisterOwnerAsync();
        var petId = await CreatePetAsync(owner.Token);

        await using var scope = fixture.Services.CreateAsyncScope();
        var pets = scope.ServiceProvider.GetRequiredService<PetsDbContext>();

        var act = () => pets.Database.ExecuteSqlAsync($"UPDATE pet.pets SET host_id = {Guid.CreateVersion7()} WHERE id = {petId}", Ct);

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_pets_one_keeper");
    }

    // ----- microchip único -----

    [Fact]
    public async Task Register_Should_Return409_When_AnotherActivePetHasTheMicrochip()
    {
        var owner = await RegisterOwnerAsync();
        var other = await RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35");
        await CreatePetAsync(owner.Token, Pipoca(microchip: Microchip));

        var response = await SendAsync(HttpMethod.Post, PetsRoute, other.Token, Pipoca(name: "Rex", microchip: "985112004567890"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("PET_MICROCHIP_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Register_Should_AcceptTheMicrochip_When_ThePetThatHadItIsDeactivated()
    {
        // O animal mudou de tutor: o antigo desativa, o novo cadastra com o mesmo chip.
        var owner = await RegisterOwnerAsync();
        var other = await RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35");
        var oldPet = await CreatePetAsync(owner.Token, Pipoca(microchip: Microchip));
        await SendAsync(HttpMethod.Post, $"{PetsRoute}/{oldPet}/deactivation", owner.Token);

        var response = await SendAsync(HttpMethod.Post, PetsRoute, other.Token, Pipoca(microchip: Microchip));
        var reactivated = await SendAsync(HttpMethod.Delete, $"{PetsRoute}/{oldPet}/deactivation", owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        reactivated.StatusCode.Should().Be(HttpStatusCode.Conflict, "o número agora é do pet ativo do outro tutor");
        Code(await ReadAsync(reactivated)).Should().Be("PET_MICROCHIP_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Update_Should_Return409_When_TheNewMicrochipIsOfAnotherActivePet()
    {
        var owner = await RegisterOwnerAsync();
        await CreatePetAsync(owner.Token, Pipoca(microchip: Microchip));
        var mia = await CreatePetAsync(owner.Token, Pipoca(species: "cat", size: null, name: "Mia"));

        var response = await SendAsync(HttpMethod.Patch, $"{PetsRoute}/{mia}", owner.Token, new { microchip = Microchip });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("PET_MICROCHIP_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Database_Should_RejectTwoActivePetsWithTheSameMicrochip()
    {
        var owner = await RegisterOwnerAsync();
        await CreatePetAsync(owner.Token, Pipoca(microchip: Microchip));
        var mia = await CreatePetAsync(owner.Token, Pipoca(species: "cat", size: null, name: "Mia"));

        await using var scope = fixture.Services.CreateAsyncScope();
        var pets = scope.ServiceProvider.GetRequiredService<PetsDbContext>();

        var act = () => pets.Database.ExecuteSqlAsync($"UPDATE pet.pets SET microchip = '985112004567890' WHERE id = {mia}", Ct);

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("uq_pets_microchip_active");
    }

    // ----- helpers -----

    private sealed record RegisteredOwner(string Token, Guid OwnerId);

    private sealed record RegisteredHost(string Token, Guid HostId);

    private async Task<RegisteredOwner> RegisterOwnerAsync(string email = "camila@exemplo.com", string cpf = "529.982.247-25")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/owners/register", new
        {
            fullName = email.StartsWith("camila", StringComparison.Ordinal) ? "Camila Souza" : "João Lima",
            email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf,
            address = Address(),
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var data = (await ReadAsync(response)).GetProperty("data");

        return new RegisteredOwner(
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("owner").GetProperty("id").GetGuid());
    }

    private async Task<(string Token, Guid UserId)> RegisterHostAccountAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "Dona Cida",
            email = "cida@exemplo.com",
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1965-04-21",
            address = Address(),
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var data = (await ReadAsync(response)).GetProperty("data");

        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("user").GetProperty("id").GetGuid());
    }

    /// <summary>Conta de anfitrião + perfil gravado direto na tabela (a rota de cadastro ainda não existe).</summary>
    private async Task<RegisteredHost> RegisterHostAsync()
    {
        var (token, userId) = await RegisterHostAccountAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        var host = Host.CreateIndividual(userId, Cpf.Create("52998224725").Value!, DateTimeOffset.UtcNow);
        hosts.Hosts.Add(host);
        await hosts.SaveChangesAsync(Ct);

        return new RegisteredHost(token, host.Id.Value);
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = PetsApiFixture.AdminEmail, password = PetsApiFixture.AdminPassword, role = "admin" },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> CreatePetAsync(string token, object? pet = null)
    {
        var response = await SendAsync(HttpMethod.Post, PetsRoute, token, pet ?? Pipoca());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("id").GetGuid();
    }

    private static object Pipoca(
        string species = "dog",
        string? size = "medium",
        string name = "Pipoca",
        string? speciesDescription = null,
        string? microchip = null) =>
        new
        {
            species,
            speciesDescription,
            name,
            breed = "SRD",
            size,
            birthDate = "2022-03-15",
            sex = "female",
            isNeutered = true,
            isVaccinated = true,
            feedingNotes = "Ração 2x ao dia",
            goodWithDogs = true,
            goodWithCats = false,
            goodWithKids = true,
            vetContact = "Dra. Ana — (44) 3222-0000",
            notes = "Morre de medo de fogos.",
            weightKg = 14.5,
            microchip,
            allergies = "Frango",
        };

    private static object Address() =>
        new
        {
            zipCode = "87020-000",
            street = "Rua das Flores",
            number = "120",
            complement = "Apto 3",
            neighborhood = "Zona 7",
            city = "Maringá",
            state = "PR",
        };

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    private static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];
}
