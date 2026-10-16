using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Infrastructure.Persistence;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.TestKit;

namespace PetHost.Modules.Conversations.IntegrationTests;

/// <summary>
/// Monta o cenário dos testes pela API, como os do Bookings: a Camila (tutora, com a Pipoca) e a
/// Dona Cida (anfitriã, gravada direto em <c>host.hosts</c> já verificada e com repasses liberados,
/// com um cantinho). Também lê o envelope das respostas.
/// </summary>
internal sealed class ConversationsScenario(ConversationsApiFixture fixture, HttpClient client)
{
    public const string ConversationsRoute = "/api/v1/conversations";
    public const string Password = "Nova@Senha123";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] DogsAndCats = ["dog", "cat"];
    private static readonly string[] EveryDay = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    /// <summary>Tutora, anfitriã e o cantinho dela.</summary>
    public sealed record Pair(string OwnerToken, string HostToken, Guid ListingId);

    public async Task<Pair> ArrangeAsync()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25", "Camila Souza");
        var host = await RegisterHostAsync();
        var listing = await CreateListingAsync(host);

        return new Pair(owner, host, listing);
    }

    /// <summary>A Camila puxa conversa no cantinho; devolve o id da conversa.</summary>
    public async Task<Guid> StartAsync(Pair pair)
    {
        var response = await SendAsync(HttpMethod.Post, ConversationsRoute, pair.OwnerToken, new { listingId = pair.ListingId });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        return Data(await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Envia uma mensagem de texto e devolve a mensagem criada.</summary>
    public async Task<JsonElement> SendTextAsync(string token, Guid conversationId, string body)
    {
        var response = await SendAsync(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages", token, new { body });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response));
    }

    /// <summary>Envia uma foto (PNG de teste) com legenda.</summary>
    public Task<HttpResponseMessage> SendPhotoAsync(string token, Guid conversationId, string? caption, int variant = 1)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.PngVariant(variant));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "pipoca.png");

        if (caption is not null)
            form.Add(new StringContent(caption), "body");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages/photo") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client.SendAsync(request, Ct);
    }

    /// <summary>
    /// Uma estadia em andamento da dupla: pedido para hoje, aceite (sem o pagamento, aceitar já
    /// confirma) e check-in. Devolve o id da reserva.
    /// </summary>
    public async Task<Guid> StartStayAsync(Pair pair)
    {
        var bookingId = await RequestStayAsync(pair);

        (await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/acceptance", pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/check-in", pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        return bookingId;
    }

    /// <summary>Um pedido de estadia (fica em aberto, <c>requested</c>) da Pipoca para hoje.</summary>
    public async Task<Guid> RequestStayAsync(Pair pair)
    {
        var petId = await RegisterPetAsync(pair.OwnerToken, "Pipoca");
        var today = BookingClock.Today(DateTimeOffset.UtcNow);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/bookings", pair.OwnerToken, new
        {
            listingId = pair.ListingId,
            petIds = new[] { petId },
            checkIn = Iso(today),
            checkOut = Iso(today.AddDays(2)),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    public async Task<string> RegisterOwnerAsync(string email, string cpf, string fullName)
    {
        var response = await client.PostAsJsonAsync("/api/v1/owners/register", new
        {
            fullName,
            email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf,
            address = new { zipCode = "87020-000", street = "Rua Joubert de Carvalho", number = "500", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>A Dona Cida: conta de anfitriã, verificada e com os repasses liberados pelo Stripe.</summary>
    public async Task<string> RegisterHostAsync()
    {
        var response = await client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "Dona Cida",
            email = "cida@exemplo.com",
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1965-04-21",
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var data = Data(await ReadAsync(response));
        var userId = data.GetProperty("user").GetProperty("id").GetGuid();

        var host = Host.CreateIndividual(userId, Cpf.Create("12345678909").Value!, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        host.LinkStripeAccount($"acct_test_{host.Id.Value:N}", DateTimeOffset.UtcNow);
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(true, true, true, null), DateTimeOffset.UtcNow);

        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        hosts.Hosts.Add(host);
        await hosts.SaveChangesAsync(Ct);

        return data.GetProperty("accessToken").GetString()!;
    }

    /// <summary>Suspende a Camila direto em <c>owner.owners</c> (o chat só olha o perfil de tutor).</summary>
    public async Task SuspendTheOwnerAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var owners = scope.ServiceProvider.GetRequiredService<OwnersDbContext>();
        owners.Owners.Single().Suspend(DateTimeOffset.UtcNow);
        await owners.SaveChangesAsync(Ct);
    }

    /// <summary>Suspende a Dona Cida direto em <c>host.hosts</c> (o chat só olha o perfil de anfitrião).</summary>
    public async Task SuspendTheHostAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        hosts.Hosts.Single().Suspend(DateTimeOffset.UtcNow);
        await hosts.SaveChangesAsync(Ct);
    }

    /// <summary>Uma conta de anfitrião sem perfil: cadastrou, mas o admin ainda não aprovou.</summary>
    public async Task<string> RegisterPendingHostAsync()
    {
        var response = await client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "Seu Jorge",
            email = "jorge@exemplo.com",
            password = Password,
            phone = "(44) 98888-0000",
            birthDate = "1970-08-02",
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "300", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>A última atividade gravada na conversa e o horário da mensagem mais nova dela.</summary>
    public async Task<(DateTimeOffset? LastMessageAt, DateTimeOffset NewestMessage)> LastActivityAsync(Guid conversationId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var conversations = scope.ServiceProvider.GetRequiredService<ConversationsDbContext>();
        var id = new ConversationId(conversationId);

        var lastMessageAt = await conversations.Conversations.AsNoTracking().Where(c => c.Id == id).Select(c => c.LastMessageAt).SingleAsync(Ct);
        var newest = await conversations.Messages.AsNoTracking().Where(m => m.ConversationId == id).MaxAsync(m => m.CreatedAt, Ct);

        return (lastMessageAt, newest);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return client.SendAsync(request, Ct);
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    public static JsonElement Data(JsonElement body) => body.GetProperty("data");

    public static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    public static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];

    private async Task<Guid> RegisterPetAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/pets", token, new
        {
            species = "dog",
            name,
            breed = "SRD",
            size = "medium",
            birthDate = "2022-03-15",
            sex = "female",
            isNeutered = true,
            isVaccinated = true,
            goodWithDogs = true,
            goodWithCats = false,
            goodWithKids = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateListingAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/listings", token, new
        {
            title = "Casa com quintal",
            description = "Casa térrea, quintal gramado e muito carinho.",
            homeType = "house",
            hasYard = true,
            hasDogs = true,
            hasCats = false,
            hasKids = false,
            acceptedSpecies = DogsAndCats,
            acceptsSmall = true,
            acceptsMedium = true,
            acceptsLarge = false,
            capacity = 3,
            nightlyRate = 60.50m,
            availableWeekdays = EveryDay,
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
