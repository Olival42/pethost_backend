using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Reviews.IntegrationTests;

/// <summary>
/// Monta o cenário dos testes pela API, como os do Conversations: a Camila (tutora, com a Pipoca) e
/// a Dona Cida (anfitriã, gravada direto em <c>host.hosts</c> já verificada e com repasses
/// liberados, com um cantinho), e a estadia da Pipoca até o check-out. Também lê o envelope.
/// </summary>
internal sealed class ReviewsScenario(ReviewsApiFixture fixture, HttpClient client)
{
    public const string ReviewsRoute = "/api/v1/reviews";
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

    /// <summary>Uma estadia concluída (check-out feito agora). Devolve o id da reserva.</summary>
    public async Task<Guid> CompleteStayAsync(Pair pair)
    {
        var bookingId = await StartStayAsync(pair);

        (await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/check-out", pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        return bookingId;
    }

    /// <summary>
    /// Uma estadia em andamento: pedido com antecedência, aceite, 1ª parcela paga (o Payments confirma
    /// pelo contrato <see cref="IBookingPaymentOutcomes"/>), o dia da chegada (por SQL) e check-in.
    /// </summary>
    public async Task<Guid> StartStayAsync(Pair pair)
    {
        var petId = await RegisterPetAsync(pair.OwnerToken, "Pipoca");
        var arrival = BookingClock.Today(DateTimeOffset.UtcNow).AddDays(4);

        var requested = await SendAsync(HttpMethod.Post, "/api/v1/bookings", pair.OwnerToken, new
        {
            listingId = pair.ListingId,
            petIds = new[] { petId },
            checkIn = Iso(arrival),
            checkOut = Iso(arrival.AddDays(2)),
        });
        requested.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookingId = Data(await ReadAsync(requested)).GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/acceptance", pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var outcomes = scope.ServiceProvider.GetRequiredService<IBookingPaymentOutcomes>();
            (await outcomes.ConfirmPaidAsync(bookingId, Ct)).Should().Be(BookingPaymentOutcome.Done);
        }

        var today = BookingClock.Today(DateTimeOffset.UtcNow);
        await fixture.ExecuteAsync(
            $"UPDATE booking.bookings SET check_in_date = '{Iso(today)}', check_out_date = '{Iso(today.AddDays(2))}' WHERE id = '{bookingId}'");

        (await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/check-in", pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        return bookingId;
    }

    /// <summary>Entra como o admin do seed.</summary>
    public async Task<string> AdminTokenAsync()
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = ReviewsApiFixture.AdminEmail, password = ReviewsApiFixture.AdminPassword, role = "admin" },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await ReadAsync(response)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>O anfitrião (<c>host.hosts.id</c>) do cantinho da dupla.</summary>
    public async Task<Guid> HostIdAsync(Pair pair)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/listings/{pair.ListingId}", pair.HostToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await ReadAsync(response)).GetProperty("hostId").GetGuid();
    }

    /// <summary>A anfitriã avalia a Camila e dá nota aos pets da reserva.</summary>
    public async Task<HttpResponseMessage> HostReviewAsync(Pair pair, Guid bookingId, int rating, object[]? pets = null) =>
        await SendAsync(HttpMethod.Post, ReviewsRoute, pair.HostToken, new { bookingId, rating, comment = "Tutora pontual.", pets });

    /// <summary>Os pets da reserva, lidos pelo anfitrião.</summary>
    public async Task<Guid[]> BookingPetIdsAsync(Pair pair, Guid bookingId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", pair.HostToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return [.. Data(await ReadAsync(response)).GetProperty("pets").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())];
    }

    /// <summary>Avalia a reserva e devolve a resposta (o teste confere o status).</summary>
    public Task<HttpResponseMessage> ReviewAsync(string token, Guid bookingId, int? rating, string? comment = null) =>
        SendAsync(HttpMethod.Post, ReviewsRoute, token, new { bookingId, rating, comment });

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
