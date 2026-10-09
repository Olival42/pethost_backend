using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using Xunit;

namespace PetHost.Modules.Owners.IntegrationTests.Owners;

/// <summary>
/// Tutor ponta a ponta pela API: cadastro unificado (conta no Auth + perfil no Owners),
/// PATCH, inativar/reativar e consultas. E o cadastro de anfitrião, que fica em
/// <c>/users/register</c>.
/// </summary>
[Collection(OwnersApiCollection.Name)]
public sealed class OwnerRegistrationTests(OwnersApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string HostRegistrationRoute = "/api/v1/users/register";
    private const string OwnersRoute = "/api/v1/owners";
    private const string LoginRoute = "/api/v1/auth/sessions/login";
    private const string Password = "Nova@Senha123";

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

    // ----- cadastro de anfitrião (/users/register) -----

    [Fact]
    public async Task RegisterHost_Should_Return201WithHostAccount_When_DataIsValid()
    {
        var response = await _client.PostAsJsonAsync(HostRegistrationRoute, HostAccount(), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var user = (await ReadAsync(response)).GetProperty("data").GetProperty("user");
        user.GetProperty("role").GetString().Should().Be("host");
        user.GetProperty("phone").GetString().Should().Be("44999990000");
        user.GetProperty("birthDate").GetString().Should().Be("1990-05-10");
        user.GetProperty("isActive").GetBoolean().Should().BeTrue();
        user.GetProperty("address").GetProperty("zipCode").GetString().Should().Be("87020000");
    }

    [Fact]
    public async Task RegisterHost_Should_IgnoreRole_When_ClientSendsOwner()
    {
        // Conta de tutor sem CPF não pode nascer por aqui: o papel é sempre host.
        var response = await _client.PostAsJsonAsync(HostRegistrationRoute, new
        {
            fullName = "Camila Souza",
            email = "camila@exemplo.com",
            password = Password,
            role = "owner",
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            address = Address(),
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync(response)).GetProperty("data").GetProperty("user").GetProperty("role").GetString().Should().Be("host");
    }

    [Fact]
    public async Task RegisterHost_Should_Return400WithEveryInvalidField_When_PayloadIsEmpty()
    {
        var response = await _client.PostAsJsonAsync(HostRegistrationRoute, new { }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should()
            .BeEquivalentTo(["fullName", "email", "password", "phone", "birthDate", "address"]);
    }

    [Fact]
    public async Task RegisterHost_Should_Return400WithNestedAddressFields_When_AddressIsInvalid()
    {
        var payload = HostAccount(address: new { zipCode = "123", street = "", number = "1", neighborhood = "Zona 7", city = "Maringá", state = "XX" });

        var response = await _client.PostAsJsonAsync(HostRegistrationRoute, payload, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Contain(["address.zipCode", "address.street", "address.state"]);
    }

    [Fact]
    public async Task RegisterHost_Should_Return400_When_PersonIsUnder18()
    {
        var seventeen = DateTime.UtcNow.AddYears(-17).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var response = await _client.PostAsJsonAsync(HostRegistrationRoute, HostAccount(birthDate: seventeen), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Contain("birthDate");
    }

    [Fact]
    public async Task RegisterHost_Should_Return409_When_EmailIsAlreadyAHost()
    {
        await _client.PostAsJsonAsync(HostRegistrationRoute, HostAccount(), Ct);

        var response = await _client.PostAsJsonAsync(HostRegistrationRoute, HostAccount(), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("AUTH_EMAIL_ALREADY_REGISTERED");
    }

    // ----- cadastro de tutor: conta + CPF num pedido só -----

    [Fact]
    public async Task RegisterOwnerAccount_Should_Return201WithSessionAndOwner_When_DataIsValid()
    {
        var response = await _client.PostAsJsonAsync($"{OwnersRoute}/register", OwnerAccount(), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Be("/api/v1/owners/me");

        var data = (await ReadAsync(response)).GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        var owner = data.GetProperty("owner");
        owner.GetProperty("cpf").GetString().Should().Be("52998224725");
        owner.GetProperty("isActive").GetBoolean().Should().BeTrue();
        owner.GetProperty("user").GetProperty("role").GetString().Should().Be("owner");
        owner.GetProperty("userId").GetGuid().Should().Be(owner.GetProperty("user").GetProperty("id").GetGuid());
        owner.GetProperty("id").GetGuid().Should().NotBe(owner.GetProperty("userId").GetGuid());

        // O token já abre a conta de tutor completa.
        var me = await SendAsync(HttpMethod.Get, $"{OwnersRoute}/me", data.GetProperty("accessToken").GetString()!);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RegisterOwnerAccount_Should_Return400WithAccountAndCpfFields_When_PayloadIsEmpty()
    {
        var response = await _client.PostAsJsonAsync($"{OwnersRoute}/register", new { }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should()
            .Contain(["fullName", "email", "password", "phone", "birthDate", "address", "cpf"]);
    }

    [Fact]
    public async Task RegisterOwnerAccount_Should_Return409_When_CpfIsAlreadyRegistered()
    {
        await RegisterOwnerAsync();

        var response = await _client.PostAsJsonAsync($"{OwnersRoute}/register", OwnerAccount(email: "outra@exemplo.com"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("OWNER_CPF_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task RegisterOwnerAccount_Should_Return409WithoutCreatingOwner_When_EmailIsAlreadyAnOwner()
    {
        await RegisterOwnerAsync();

        var response = await _client.PostAsJsonAsync($"{OwnersRoute}/register", OwnerAccount(cpf: "111.444.777-35"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("AUTH_EMAIL_ALREADY_REGISTERED");

        await using var scope = fixture.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<OwnersDbContext>().Owners.CountAsync(Ct)).Should().Be(1);
    }

    // ----- PATCH /owners/me -----

    [Fact]
    public async Task UpdateMe_Should_ChangeOnlyTheSentFields_When_PatchIsPartial()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new
        {
            fullName = "Camila Lima",
            address = new { number = "300", complement = "" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ReadAsync(response)).GetProperty("data");
        data.GetProperty("cpf").GetString().Should().Be("52998224725", "CPF não veio, não muda");
        var user = data.GetProperty("user");
        user.GetProperty("fullName").GetString().Should().Be("Camila Lima");
        user.GetProperty("phone").GetString().Should().Be("44999990000", "telefone não veio, não muda");
        var address = user.GetProperty("address");
        address.GetProperty("number").GetString().Should().Be("300");
        address.GetProperty("street").GetString().Should().Be("Rua das Flores");
        // A API omite campo nulo: complemento limpo some da resposta.
        address.TryGetProperty("complement", out _).Should().BeFalse("texto vazio limpa o complemento");
    }

    [Fact]
    public async Task UpdateMe_Should_ChangeAccountAndCpfTogether_When_PasswordIsRight()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new
        {
            phone = "(44) 3222-1111",
            cpf = "111.444.777-35",
            currentPassword = Password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ReadAsync(response)).GetProperty("data");
        data.GetProperty("cpf").GetString().Should().Be("11144477735");
        data.GetProperty("user").GetProperty("phone").GetString().Should().Be("4432221111");
    }

    [Fact]
    public async Task UpdateMe_Should_Return400OnCurrentPassword_When_CpfChangesWithoutPassword()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new
        {
            fullName = "Camila Lima",
            cpf = "111.444.777-35",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("currentPassword");

        var me = (await ReadAsync(await SendAsync(HttpMethod.Get, $"{OwnersRoute}/me", owner.Token))).GetProperty("data");
        me.GetProperty("user").GetProperty("fullName").GetString().Should().Be("Camila Souza", "pedido recusado não grava nada");
    }

    [Fact]
    public async Task UpdateMe_Should_Return400OnCurrentPassword_When_PasswordIsWrong()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new
        {
            cpf = "111.444.777-35",
            currentPassword = "Errada@123",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("currentPassword");
    }

    [Fact]
    public async Task UpdateMe_Should_Return400WithEveryField_When_AccountAndCpfAreInvalid()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new
        {
            fullName = "",
            phone = "abc",
            address = new { zipCode = "1" },
            cpf = "111.111.111-11",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Contain(["fullName", "phone", "address.zipCode", "cpf"]);
    }

    [Fact]
    public async Task UpdateMe_Should_Return422_When_FirstPaymentHappened()
    {
        var owner = await RegisterOwnerAsync();
        await SimulateFirstPaymentAsync(owner.UserId);

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new
        {
            cpf = "111.444.777-35",
            currentPassword = Password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("OWNER_CPF_LOCKED");
    }

    [Fact]
    public async Task UpdateMe_Should_Return409_When_CpfBelongsToAnotherOwner()
    {
        await RegisterOwnerAsync();
        var second = await RegisterOwnerAsync(email: "outra@exemplo.com", cpf: "111.444.777-35");

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", second.Token, new
        {
            cpf = "529.982.247-25",
            currentPassword = Password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("OWNER_CPF_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task UpdateMe_Should_ChangeBirthDate_When_PasswordIsRight()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new
        {
            birthDate = "1991-02-03",
            currentPassword = Password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("data").GetProperty("user").GetProperty("birthDate").GetString()
            .Should().Be("1991-02-03");
    }

    [Fact]
    public async Task UpdateMe_Should_Return400_When_BirthDateChangesWithoutPassword()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new { birthDate = "1991-02-03" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("currentPassword");
    }

    [Fact]
    public async Task UpdateMe_Should_Return422_When_BirthDateChangesAfterFirstPayment()
    {
        var owner = await RegisterOwnerAsync();
        await SimulateFirstPaymentAsync(owner.UserId);

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new
        {
            birthDate = "1991-02-03",
            currentPassword = Password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("OWNER_BIRTH_DATE_LOCKED");
    }

    [Fact]
    public async Task UpdateMe_Should_AcceptResentData_When_ValuesAreTheSame()
    {
        // Mandar o que já está cadastrado não é troca: sem senha, e nem a trava do pagamento conta.
        var owner = await RegisterOwnerAsync();
        await SimulateFirstPaymentAsync(owner.UserId);

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new
        {
            fullName = "Camila Souza",
            birthDate = "1990-05-10",
            cpf = "529.982.247-25",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateMe_Should_Return403_When_LoggedInAsHost()
    {
        var host = await RegisterHostAsync();

        var response = await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", host, new { fullName = "Cida" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- inativar e reativar -----

    [Fact]
    public async Task DeactivateMe_Should_DeactivateOwnerAndAccount_When_Called()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Post, $"{OwnersRoute}/me/deactivate", owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Get, $"{OwnersRoute}/me", owner.Token)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "as sessões caem na hora");

        var login = await LoginAsync("camila@exemplo.com", Password);
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(login)).Should().Be("AUTH_ACCOUNT_DEACTIVATED");

        await using var scope = fixture.Services.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<OwnersDbContext>().Owners.AsNoTracking().SingleAsync(Ct);
        saved.IsActive.Should().BeFalse();
        saved.DeactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reactivate_Should_ReactivateOwnerAndAccountAndReturnSession_When_CredentialsAreRight()
    {
        var owner = await RegisterOwnerAsync();
        await SendAsync(HttpMethod.Post, $"{OwnersRoute}/me/deactivate", owner.Token);

        var response = await _client.PostAsJsonAsync(
            $"{OwnersRoute}/reactivate",
            new { email = "camila@exemplo.com", password = Password },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ReadAsync(response)).GetProperty("data");
        data.GetProperty("owner").GetProperty("isActive").GetBoolean().Should().BeTrue();
        data.GetProperty("owner").GetProperty("user").GetProperty("isActive").GetBoolean().Should().BeTrue();

        var me = await SendAsync(HttpMethod.Get, $"{OwnersRoute}/me", data.GetProperty("accessToken").GetString()!);
        me.StatusCode.Should().Be(HttpStatusCode.OK, "a sessão nova vale, mesmo emitida logo depois da revogação");
    }

    [Fact]
    public async Task Reactivate_Should_Return401AndKeepInactive_When_PasswordIsWrong()
    {
        var owner = await RegisterOwnerAsync();
        await SendAsync(HttpMethod.Post, $"{OwnersRoute}/me/deactivate", owner.Token);

        var response = await _client.PostAsJsonAsync(
            $"{OwnersRoute}/reactivate",
            new { email = "camila@exemplo.com", password = "Errada@123" },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var scope = fixture.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<OwnersDbContext>().Owners.AsNoTracking().SingleAsync(Ct))
            .IsActive.Should().BeFalse();
    }

    // ----- consultas -----

    [Fact]
    public async Task GetMe_Should_ReturnOwnerWithAccount_When_LoggedIn()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Get, $"{OwnersRoute}/me", owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ReadAsync(response)).GetProperty("data");
        data.GetProperty("id").GetGuid().Should().Be(owner.OwnerId);
        data.GetProperty("userId").GetGuid().Should().Be(owner.UserId);
        data.GetProperty("cpf").GetString().Should().Be("52998224725");
        data.TryGetProperty("canChangeCpf", out _).Should().BeFalse();
        data.GetProperty("user").GetProperty("address").GetProperty("city").GetString().Should().Be("Maringá");
    }

    [Fact]
    public async Task AdminQueries_Should_ReturnOwnersWithAccountData_When_CalledByAdmin()
    {
        var owner = await RegisterOwnerAsync();
        var admin = await AdminTokenAsync();

        var list = await SendAsync(HttpMethod.Get, OwnersRoute, admin);
        var byId = await SendAsync(HttpMethod.Get, $"{OwnersRoute}/{owner.OwnerId}", admin);

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = (await ReadAsync(list)).GetProperty("data").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("user").GetProperty("fullName").GetString().Should().Be("Camila Souza");
        item.GetProperty("user").GetProperty("email").GetString().Should().Be("camila@exemplo.com");

        byId.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(byId)).GetProperty("data").GetProperty("cpf").GetString().Should().Be("52998224725");
    }

    [Fact]
    public async Task AdminQueries_Should_Return403_When_CalledByOwner()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Get, OwnersRoute, owner.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- helpers -----

    private sealed record RegisteredOwner(string Token, Guid UserId, Guid OwnerId);

    private async Task<RegisteredOwner> RegisterOwnerAsync(string email = "camila@exemplo.com", string cpf = "529.982.247-25")
    {
        var response = await _client.PostAsJsonAsync($"{OwnersRoute}/register", OwnerAccount(email, cpf), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var data = (await ReadAsync(response)).GetProperty("data");
        var owner = data.GetProperty("owner");

        return new RegisteredOwner(
            data.GetProperty("accessToken").GetString()!,
            owner.GetProperty("userId").GetGuid(),
            owner.GetProperty("id").GetGuid());
    }

    private async Task<string> RegisterHostAsync()
    {
        var response = await _client.PostAsJsonAsync(HostRegistrationRoute, HostAccount(), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync(LoginRoute, new { email, password, role = "owner" }, Ct);

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            LoginRoute,
            new { email = OwnersApiFixture.AdminEmail, password = OwnersApiFixture.AdminPassword, role = "admin" },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    /// <summary>O módulo de pagamentos ainda não existe: liga o Customer direto no banco.</summary>
    private async Task SimulateFirstPaymentAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var owners = scope.ServiceProvider.GetRequiredService<OwnersDbContext>();
        var owner = await owners.Owners.SingleAsync(o => o.UserId == userId, Ct);

        owner.LinkStripeCustomer("cus_test_123", DateTimeOffset.UtcNow);
        await owners.SaveChangesAsync(Ct);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

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

    private static object OwnerAccount(string email = "camila@exemplo.com", string cpf = "529.982.247-25") =>
        new
        {
            fullName = "Camila Souza",
            email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf,
            address = Address(),
        };

    private static object HostAccount(string birthDate = "1990-05-10", object? address = null) =>
        new
        {
            fullName = "Dona Cida",
            email = "cida@exemplo.com",
            password = Password,
            phone = "(44) 99999-0000",
            birthDate,
            address = address ?? Address(),
        };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    private static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];
}
