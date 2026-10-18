using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using Xunit;

namespace PetHost.Modules.Owners.IntegrationTests.Owners;

/// <summary>Exclusão da conta de tutor pela API (LGPD): anonimiza, derruba as sessões e libera o e-mail.</summary>
[Collection(OwnersApiCollection.Name)]
public sealed class OwnerDeletionTests(OwnersApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string OwnersRoute = "/api/v1/owners";
    private const string LoginRoute = "/api/v1/auth/sessions/login";
    private const string Email = "camila@exemplo.com";
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

    [Fact]
    public async Task DeleteMe_Should_AnonymizeTheAccountAndDropTheCpf_When_PasswordMatches()
    {
        var owner = await RegisterOwnerAsync();

        var response = await DeleteAsync(owner.Token, Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == new UserId(owner.UserId), Ct);
        user.FullName.Value.Should().Be(User.ErasedFullName);
        user.Email.Value.Should().EndWith($"@{User.ErasedEmailDomain}");
        user.Phone.Should().BeNull();
        user.Address.Should().BeNull();
        user.BirthDate.Should().BeNull();
        user.IsActive.Should().BeFalse();
        user.ErasedAt.Should().NotBeNull();

        var saved = await scope.ServiceProvider.GetRequiredService<OwnersDbContext>().Owners.AsNoTracking().SingleAsync(Ct);
        saved.Cpf.Should().BeNull();
        saved.IsActive.Should().BeFalse();
        saved.ErasedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteMe_Should_EndTheSessionsAndRefuseTheOldLogin_When_Deleted()
    {
        var owner = await RegisterOwnerAsync();

        await DeleteAsync(owner.Token, Password);

        (await SendAsync(HttpMethod.Get, $"{OwnersRoute}/me", owner.Token)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "as sessões caem na hora");

        var login = await _client.PostAsJsonAsync(LoginRoute, new { email = Email, password = Password, role = "owner" }, Ct);
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o e-mail não é mais de conta nenhuma");

        var reactivate = await _client.PostAsJsonAsync($"{OwnersRoute}/reactivate", new { email = Email, password = Password }, Ct);
        reactivate.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "conta excluída não volta");
    }

    [Fact]
    public async Task DeleteMe_Should_FreeTheEmailAndTheCpf_When_Deleted()
    {
        var owner = await RegisterOwnerAsync();
        await DeleteAsync(owner.Token, Password);

        // A mesma pessoa pode se cadastrar de novo do zero.
        var again = await _client.PostAsJsonAsync($"{OwnersRoute}/register", OwnerAccount(), Ct);

        again.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task DeleteMe_Should_Return400AndKeepTheAccount_When_PasswordIsWrong()
    {
        var owner = await RegisterOwnerAsync();

        var response = await DeleteAsync(owner.Token, "Errada@123");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadAsync(response);
        body.GetProperty("error").GetProperty("details")[0].GetProperty("field").GetString().Should().Be("currentPassword");

        (await SendAsync(HttpMethod.Get, $"{OwnersRoute}/me", owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMe_Should_Return400_When_PasswordIsMissing()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Post, $"{OwnersRoute}/me/deletion", owner.Token, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ----- helpers -----

    private sealed record RegisteredOwner(string Token, Guid UserId);

    private async Task<RegisteredOwner> RegisterOwnerAsync()
    {
        var response = await _client.PostAsJsonAsync($"{OwnersRoute}/register", OwnerAccount(), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var data = (await ReadAsync(response)).GetProperty("data");

        return new RegisteredOwner(
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("owner").GetProperty("userId").GetGuid());
    }

    private Task<HttpResponseMessage> DeleteAsync(string token, string password) =>
        SendAsync(HttpMethod.Post, $"{OwnersRoute}/me/deletion", token, new { currentPassword = password });

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

    private static object OwnerAccount() =>
        new
        {
            fullName = "Camila Souza",
            email = Email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf = "529.982.247-25",
            address = new
            {
                zipCode = "87020-000",
                street = "Av. Brasil",
                number = "1500",
                complement = "Apto 12",
                neighborhood = "Centro",
                city = "Maringá",
                state = "PR",
            },
        };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
}
