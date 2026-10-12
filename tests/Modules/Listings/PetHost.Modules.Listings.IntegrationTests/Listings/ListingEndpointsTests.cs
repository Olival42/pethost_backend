using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.IntegrationTests.Listings;

/// <summary>
/// Cantinhos ponta a ponta pela API: cadastro (PF com um, PJ com filiais), PATCH, listagens,
/// quem vê o quê, inativar/reativar, busca por raio e fotos com reordenação. O anfitrião é
/// gravado direto em <c>host.hosts</c>, como se a verificação já tivesse sido aprovada.
/// </summary>
[Collection(ListingsApiCollection.Name)]
public sealed class ListingEndpointsTests(ListingsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ListingsRoute = "/api/v1/listings";
    private const string Password = "Nova@Senha123";

    private static readonly string[] OnlySunday = ["sunday"];

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
    public async Task Create_Should_Return201WithTheFullListing_When_IndividualHostSendsAValidListing()
    {
        var host = await RegisterIndividualHostAsync();

        var response = await SendAsync(HttpMethod.Post, ListingsRoute, host.Token, Cantinho());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var listing = (await ReadAsync(response)).GetProperty("data");
        response.Headers.Location!.ToString().Should().Be($"{ListingsRoute}/{listing.GetProperty("id").GetGuid()}");
        listing.GetProperty("hostId").GetGuid().Should().Be(host.HostId);
        listing.GetProperty("host").GetProperty("name").GetString().Should().Be("Dona Cida");
        listing.GetProperty("homeType").GetString().Should().Be("house");
        listing.GetProperty("nightlyRate").GetDecimal().Should().Be(60.50m);
        listing.GetProperty("acceptedSpecies").EnumerateArray().Select(s => s.GetString()).Should().Equal("dog", "cat");
        listing.GetProperty("availableWeekdays").EnumerateArray().Select(s => s.GetString()).Should().Equal("monday", "saturday");
        listing.GetProperty("address").GetProperty("street").GetString().Should().Be("Rua das Flores");
        listing.GetProperty("address").GetProperty("zipCode").GetString().Should().Be("87020000");
        listing.GetProperty("location").GetProperty("latitude").GetDecimal().Should().Be(-23.41m);
        listing.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_Return422LimitReached_When_IndividualHostAlreadyHasAListing()
    {
        var host = await RegisterIndividualHostAsync();
        await CreateListingAsync(host.Token);

        var response = await SendAsync(HttpMethod.Post, ListingsRoute, host.Token, Cantinho(neighborhood: "Zona 5"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("LISTING_LIMIT_REACHED");
    }

    [Fact]
    public async Task Create_Should_AllowManyBranchesWithoutHomeTypeAndBigCapacity_When_HostIsCompany()
    {
        var company = await RegisterCompanyHostAsync();

        var first = await SendAsync(HttpMethod.Post, ListingsRoute, company.Token, Cantinho(homeType: null, capacity: 150));
        var second = await SendAsync(HttpMethod.Post, ListingsRoute, company.Token, Cantinho(neighborhood: "Zona 5", homeType: null, capacity: 80));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var listing = (await ReadAsync(first)).GetProperty("data");
        listing.TryGetProperty("homeType", out _).Should().BeFalse("empresa não precisa informar o tipo da casa");
        listing.GetProperty("capacity").GetInt32().Should().Be(150);
        listing.GetProperty("host").GetProperty("name").GetString().Should().Be("Pet Feliz", "empresa aparece pelo nome fantasia");
    }

    [Fact]
    public async Task Create_Should_Return400WithEveryRequiredField_When_BodyIsEmpty()
    {
        var host = await RegisterIndividualHostAsync();

        var response = await SendAsync(HttpMethod.Post, ListingsRoute, host.Token, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(
        [
            "title", "description", "homeType", "hasYard", "hasDogs", "hasCats", "hasKids", "acceptedSpecies",
            "acceptsSmall", "acceptsMedium", "acceptsLarge", "capacity", "nightlyRate", "availableWeekdays",
            "address.zipCode", "address.street", "address.number", "address.neighborhood", "address.city", "address.state",
        ]);
    }

    [Fact]
    public async Task Create_Should_Return400_When_IndividualSendsMoreThanTenPetsOrThreeDecimals()
    {
        var host = await RegisterIndividualHostAsync();

        var response = await SendAsync(HttpMethod.Post, ListingsRoute, host.Token, Cantinho(capacity: 11, nightlyRate: 60.555m));

        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["capacity", "nightlyRate"]);
    }

    [Theory]
    [InlineData(FakeGeocoder.NotFoundNeighborhood, HttpStatusCode.UnprocessableEntity, "LISTING_ADDRESS_NOT_LOCATED")]
    [InlineData(FakeGeocoder.UnavailableNeighborhood, HttpStatusCode.ServiceUnavailable, "LISTING_GEOCODER_UNAVAILABLE")]
    public async Task Create_Should_FailWithoutSaving_When_TheMapDoesNotLocateTheAddress(string neighborhood, HttpStatusCode status, string code)
    {
        var host = await RegisterIndividualHostAsync();

        var response = await SendAsync(HttpMethod.Post, ListingsRoute, host.Token, Cantinho(neighborhood: neighborhood));

        response.StatusCode.Should().Be(status);
        Code(await ReadAsync(response)).Should().Be(code);
        (await ListMineAsync(host.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_Should_Return404HostNotFound_When_HostAccountIsNotVerified()
    {
        var (token, _) = await RegisterHostAccountAsync("novo@exemplo.com", "Novo Anfitrião");

        var response = await SendAsync(HttpMethod.Post, ListingsRoute, token, Cantinho());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("LISTING_HOST_NOT_FOUND");
    }

    [Fact]
    public async Task Create_Should_Return403_When_CallerIsAnOwner()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Post, ListingsRoute, owner, Cantinho());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- leitura -----

    [Fact]
    public async Task GetById_Should_HideStreetZipAndLocation_When_ViewerIsAnOwner()
    {
        var host = await RegisterIndividualHostAsync();
        var listingId = await CreateListingAsync(host.Token);
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Get, $"{ListingsRoute}/{listingId}", owner);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var listing = (await ReadAsync(response)).GetProperty("data");
        var address = listing.GetProperty("address");
        address.GetProperty("neighborhood").GetString().Should().Be("Zona 7");
        address.TryGetProperty("street", out _).Should().BeFalse();
        address.TryGetProperty("number", out _).Should().BeFalse();
        address.TryGetProperty("zipCode", out _).Should().BeFalse();
        listing.TryGetProperty("location", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ListMine_Should_ReturnEveryBranchWithTheHostOnce()
    {
        var company = await RegisterCompanyHostAsync();
        await CreateListingAsync(company.Token, Cantinho(homeType: null));
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Zona 5", homeType: null));

        var response = await SendAsync(HttpMethod.Get, $"{ListingsRoute}/me", company.Token);

        var data = (await ReadAsync(response)).GetProperty("data");
        data.GetProperty("host").GetProperty("name").GetString().Should().Be("Pet Feliz");
        data.GetProperty("listings").GetArrayLength().Should().Be(2);
        data.GetProperty("listings")[0].TryGetProperty("host", out _).Should().BeFalse();
    }

    // ----- inativar e reativar -----

    [Fact]
    public async Task Deactivation_Should_HideTheListingFromOthersButNotFromTheOwner_And_DeleteShouldBringItBack()
    {
        var host = await RegisterIndividualHostAsync();
        var listingId = await CreateListingAsync(host.Token);
        var owner = await RegisterOwnerAsync();

        var deactivated = await SendAsync(HttpMethod.Post, $"{ListingsRoute}/{listingId}/deactivation", host.Token);
        var seenByOwner = await SendAsync(HttpMethod.Get, $"{ListingsRoute}/{listingId}", owner);
        var seenByHost = await SendAsync(HttpMethod.Get, $"{ListingsRoute}/{listingId}", host.Token);
        var reactivated = await SendAsync(HttpMethod.Delete, $"{ListingsRoute}/{listingId}/deactivation", host.Token);
        var seenAgain = await SendAsync(HttpMethod.Get, $"{ListingsRoute}/{listingId}", owner);

        (await ReadAsync(deactivated)).GetProperty("data").GetProperty("isActive").GetBoolean().Should().BeFalse();
        seenByOwner.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(seenByOwner)).Should().Be("LISTING_NOT_FOUND");
        seenByHost.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(reactivated)).GetProperty("data").GetProperty("isActive").GetBoolean().Should().BeTrue();
        seenAgain.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deactivation_Should_Return404_When_ListingIsFromAnotherHost()
    {
        var host = await RegisterIndividualHostAsync();
        var listingId = await CreateListingAsync(host.Token);
        var company = await RegisterCompanyHostAsync();

        var response = await SendAsync(HttpMethod.Post, $"{ListingsRoute}/{listingId}/deactivation", company.Token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("LISTING_NOT_FOUND");
    }

    // ----- alteração -----

    [Fact]
    public async Task Update_Should_ChangeTheRateAndRelocate_When_RateAndNeighborhoodAreSent()
    {
        var host = await RegisterIndividualHostAsync();
        var listingId = await CreateListingAsync(host.Token);

        var response = await SendAsync(
            HttpMethod.Patch,
            $"{ListingsRoute}/{listingId}",
            host.Token,
            new { nightlyRate = 75.00m, availableWeekdays = OnlySunday, address = new { neighborhood = "Zona 5" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var listing = (await ReadAsync(response)).GetProperty("data");
        listing.GetProperty("nightlyRate").GetDecimal().Should().Be(75m);
        listing.GetProperty("availableWeekdays").EnumerateArray().Select(d => d.GetString()).Should().Equal("sunday");
        listing.GetProperty("address").GetProperty("street").GetString().Should().Be("Rua das Flores");
        listing.GetProperty("location").GetProperty("latitude").GetDecimal().Should().Be(-23.418m);
    }

    // ----- busca por raio -----

    [Fact]
    public async Task Search_Should_ReturnOnlyListingsInsideTheRadius_OrderedByDistance()
    {
        var cida = await RegisterIndividualHostAsync();
        await CreateListingAsync(cida.Token, Cantinho(neighborhood: "Zona 5"));
        var company = await RegisterCompanyHostAsync();
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Vila Sarandi", homeType: null));
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Centro de Londrina", homeType: null));
        var owner = await RegisterOwnerAsync();

        var within5 = await SearchAsync(owner, "radiusKm=5&latitude=-23.41&longitude=-51.933");
        var within10 = await SearchAsync(owner, "radiusKm=10&latitude=-23.41&longitude=-51.933");
        var within50 = await SearchAsync(owner, "radiusKm=50&latitude=-23.41&longitude=-51.933");

        Neighborhoods(within5).Should().Equal("Zona 5");
        Neighborhoods(within10).Should().Equal("Zona 5", "Vila Sarandi");
        Neighborhoods(within50).Should().Equal("Zona 5", "Vila Sarandi");
        within10.GetProperty("totalCount").GetInt64().Should().Be(2);
        var first = within10.GetProperty("items")[0];
        first.GetProperty("distanceKm").GetDouble().Should().BeApproximately(1.2, 0.2);
        first.TryGetProperty("street", out _).Should().BeFalse();
        first.TryGetProperty("latitude", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Search_Should_UseTheAccountAddress_When_NoCoordinatesAreSent()
    {
        var cida = await RegisterIndividualHostAsync();
        await CreateListingAsync(cida.Token, Cantinho(neighborhood: "Zona 5"));
        var owner = await RegisterOwnerAsync();

        var result = await SearchAsync(owner, "radiusKm=5");

        Neighborhoods(result).Should().Equal("Zona 5");
    }

    [Fact]
    public async Task Search_Should_HideInactiveListingsAndSuspendedHosts()
    {
        var cida = await RegisterIndividualHostAsync();
        var hidden = await CreateListingAsync(cida.Token, Cantinho(neighborhood: "Zona 5"));
        await SendAsync(HttpMethod.Post, $"{ListingsRoute}/{hidden}/deactivation", cida.Token);
        var company = await RegisterCompanyHostAsync();
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Vila Sarandi", homeType: null));
        await SuspendHostAsync(company.HostId);
        var owner = await RegisterOwnerAsync();

        var result = await SearchAsync(owner, "radiusKm=20&latitude=-23.41&longitude=-51.933");

        result.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Create_Should_Return422_When_StripeHasNotApprovedThePayoutSetupYet()
    {
        var cida = await RegisterIndividualHostAsync(payoutsEnabled: false);

        var response = await SendAsync(HttpMethod.Post, ListingsRoute, cida.Token, Cantinho());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadAsync(response)).GetProperty("error").GetProperty("code").GetString().Should().Be("LISTING_HOST_PAYOUTS_NOT_ENABLED");
        (await ListMineAsync(cida.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_Should_HideTheListingButKeepItForTheOwner_When_StripeBlocksPayoutsLater()
    {
        var cida = await RegisterIndividualHostAsync();
        var listingId = await CreateListingAsync(cida.Token, Cantinho(neighborhood: "Zona 5"));
        await BlockPayoutsAsync(cida.HostId);
        var owner = await RegisterOwnerAsync();

        var result = await SearchAsync(owner, "radiusKm=20&latitude=-23.41&longitude=-51.933");
        var detail = await SendAsync(HttpMethod.Get, $"{ListingsRoute}/{listingId}", owner);
        var mine = await ListMineAsync(cida.Token);

        result.GetProperty("items").GetArrayLength().Should().Be(0, "sem repasses liberados o cantinho não pode ser reservado");
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        mine.Should().ContainSingle("o anfitrião continua vendo o próprio cantinho enquanto resolve com o Stripe");
    }

    [Fact]
    public async Task Search_Should_Return400_When_RadiusIsOutOfRange()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(HttpMethod.Get, $"{ListingsRoute}?radiusKm=51&latitude=-23.41", owner);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["radiusKm", "latitude"]);
    }

    // ----- filtros -----

    [Fact]
    public async Task Search_Should_IgnoreDistance_When_OnlyPlaceFiltersAreSent()
    {
        var company = await RegisterCompanyHostAsync();
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Zona 5", homeType: null));
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Centro de Londrina", homeType: null, city: "Londrina"));
        var owner = await RegisterOwnerAsync();

        var londrina = await SearchAsync(owner, "state=pr&city=LONDRINA");
        var maringa = await SearchAsync(owner, "city=MARINGA&neighborhood=zôna 5");

        Neighborhoods(londrina).Should().Equal("Centro de Londrina");
        londrina.GetProperty("items")[0].TryGetProperty("distanceKm", out _).Should().BeFalse();
        Neighborhoods(maringa).Should().Equal("Zona 5");
    }

    [Fact]
    public async Task Search_Should_SumEveryFilter_When_FiltersAreCombinedWithTheRadius()
    {
        var company = await RegisterCompanyHostAsync();
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Zona 5", homeType: null));
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Vila Sarandi", homeType: null, nightlyRate: 90m, species: ["dog", "rabbit"], acceptsLarge: true));
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Centro de Londrina", homeType: null, city: "Londrina", nightlyRate: 90m, species: ["dog", "rabbit"], acceptsLarge: true));
        var owner = await RegisterOwnerAsync();

        var result = await SearchAsync(
            owner,
            "radiusKm=20&latitude=-23.41&longitude=-51.933&city=Maringá&species=dog,rabbit&sizes=large&weekdays=saturday&minNightlyRate=80&maxNightlyRate=100&minCapacity=3&hasYard=true");

        Neighborhoods(result).Should().Equal("Vila Sarandi");
        result.GetProperty("items")[0].GetProperty("distanceKm").GetDouble().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Search_Should_FilterByHomeTypeAndWhoLivesInTheHouse()
    {
        var company = await RegisterCompanyHostAsync();
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Zona 5", homeType: null));
        await CreateListingAsync(company.Token, Cantinho(neighborhood: "Vila Sarandi", homeType: "apartment", hasCats: true));
        await CreateListingAsync(company.Token, Cantinho(homeType: "house", hasKids: true));
        var owner = await RegisterOwnerAsync();

        var apartments = await SearchAsync(owner, "city=Maringá&homeType=apartment");
        var houses = await SearchAsync(owner, "city=Maringá&homeType=HOUSE");
        var withCatNoKids = await SearchAsync(owner, "city=Maringá&hasDogs=true&hasCats=true&hasKids=false");
        var noKids = await SearchAsync(owner, "city=Maringá&hasKids=false");

        Neighborhoods(apartments).Should().Equal("Vila Sarandi");
        Neighborhoods(houses).Should().Equal("Zona 7");
        Neighborhoods(withCatNoKids).Should().Equal("Vila Sarandi");
        Neighborhoods(noKids).Should().BeEquivalentTo(["Zona 5", "Vila Sarandi"]);
    }

    [Fact]
    public async Task Search_Should_TreatWildcardsAsText_When_FilteringByCity()
    {
        var company = await RegisterCompanyHostAsync();
        await CreateListingAsync(company.Token, Cantinho(homeType: null));
        var owner = await RegisterOwnerAsync();

        var result = await SearchAsync(owner, "city=%25");

        result.GetProperty("totalCount").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task Search_Should_Return400WithEveryField_When_FiltersAreInvalid()
    {
        var owner = await RegisterOwnerAsync();

        var response = await SendAsync(
            HttpMethod.Get,
            $"{ListingsRoute}?state=XX&species=dragon&sizes=huge&weekdays=someday&minNightlyRate=90&maxNightlyRate=10&minCapacity=0&homeType=castle",
            owner);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["state", "species", "sizes", "weekdays", "maxNightlyRate", "minCapacity", "homeType"]);
    }

    // ----- fotos -----

    [Fact]
    public async Task Photos_Should_BeAddedInOrder_Reordered_AndCompactedOnRemoval()
    {
        var host = await RegisterIndividualHostAsync();
        var listingId = await CreateListingAsync(host.Token);
        foreach (var variant in Enumerable.Range(1, 3))
            (await SendFileAsync(HttpMethod.Post, $"{ListingsRoute}/{listingId}/photos", host.Token, TestImages.PngVariant(variant))).StatusCode.Should().Be(HttpStatusCode.OK);
        var photos = await PhotoIdsAsync(host.Token, listingId);

        var reordered = await SendAsync(HttpMethod.Put, $"{ListingsRoute}/{listingId}/photos/order", host.Token, new { photoIds = new[] { photos[2], photos[0], photos[1] } });
        var removed = await SendAsync(HttpMethod.Delete, $"{ListingsRoute}/{listingId}/photos/{photos[2]}", host.Token);

        reordered.StatusCode.Should().Be(HttpStatusCode.OK);
        Photos(await ReadAsync(reordered)).Should().Equal((photos[2], 1), (photos[0], 2), (photos[1], 3));
        Photos(await ReadAsync(removed)).Should().Equal((photos[0], 1), (photos[1], 2));
        (await PhotoIdsAsync(host.Token, listingId)).Should().Equal(photos[0], photos[1]);
    }

    [Fact]
    public async Task Photos_Should_Return400_When_ReorderMissesAPhoto()
    {
        var host = await RegisterIndividualHostAsync();
        var listingId = await CreateListingAsync(host.Token);
        await SendFileAsync(HttpMethod.Post, $"{ListingsRoute}/{listingId}/photos", host.Token, TestImages.PngVariant(1));
        await SendFileAsync(HttpMethod.Post, $"{ListingsRoute}/{listingId}/photos", host.Token, TestImages.PngVariant(2));
        var photos = await PhotoIdsAsync(host.Token, listingId);

        var response = await SendAsync(HttpMethod.Put, $"{ListingsRoute}/{listingId}/photos/order", host.Token, new { photoIds = new[] { photos[1] } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("photoIds");
    }

    [Fact]
    public async Task Photos_Should_Return404PhotoNotFound_When_ReorderSendsAnUnknownId()
    {
        var host = await RegisterIndividualHostAsync();
        var listingId = await CreateListingAsync(host.Token);
        await SendFileAsync(HttpMethod.Post, $"{ListingsRoute}/{listingId}/photos", host.Token, TestImages.PngVariant(1));
        var photos = await PhotoIdsAsync(host.Token, listingId);

        var response = await SendAsync(HttpMethod.Put, $"{ListingsRoute}/{listingId}/photos/order", host.Token, new { photoIds = new[] { photos[0], Guid.CreateVersion7() } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("LISTING_PHOTO_NOT_FOUND");
    }

    [Fact]
    public async Task Photos_Should_Return422_When_ListingAlreadyHasFivePhotos()
    {
        var host = await RegisterIndividualHostAsync();
        var listingId = await CreateListingAsync(host.Token);
        foreach (var variant in Enumerable.Range(1, 5))
            await SendFileAsync(HttpMethod.Post, $"{ListingsRoute}/{listingId}/photos", host.Token, TestImages.PngVariant(variant));

        var response = await SendFileAsync(HttpMethod.Post, $"{ListingsRoute}/{listingId}/photos", host.Token, TestImages.PngVariant(6));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("LISTING_PHOTO_LIMIT_REACHED");
    }

    // ----- helpers -----

    private sealed record RegisteredHost(string Token, Guid HostId);

    private async Task<string> RegisterOwnerAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/owners/register", new
        {
            fullName = "Camila Souza",
            email = "camila@exemplo.com",
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf = "529.982.247-25",
            address = new { zipCode = "87020-000", street = "Rua Joubert de Carvalho", number = "500", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task<(string Token, Guid UserId)> RegisterHostAccountAsync(string email, string fullName)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName,
            email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1965-04-21",
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var data = (await ReadAsync(response)).GetProperty("data");

        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("user").GetProperty("id").GetGuid());
    }

    private async Task<RegisteredHost> RegisterIndividualHostAsync(bool payoutsEnabled = true)
    {
        var (token, userId) = await RegisterHostAccountAsync("cida@exemplo.com", "Dona Cida");
        var host = Host.CreateIndividual(userId, Cpf.Create("12345678909").Value!, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        return new RegisteredHost(token, await SaveHostAsync(host, payoutsEnabled));
    }

    private async Task<RegisteredHost> RegisterCompanyHostAsync()
    {
        var (token, userId) = await RegisterHostAccountAsync("hotel@petfeliz.com", "Marcos Lima");
        var host = Host.CreateCompany(
            userId,
            Cpf.Create("11144477735").Value!,
            Cnpj.Create("11222333000181").Value!,
            "Pet Feliz Hospedagem LTDA",
            "Pet Feliz",
            CompanyAddress.Create("87013000", "Av. Brasil", "1500", null, "Centro", "Maringá", "PR").Value!,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow).Value!;

        return new RegisteredHost(token, await SaveHostAsync(host));
    }

    /// <summary>
    /// Grava o anfitrião já com o cadastro de recebimento aprovado pelo Stripe (sem isso os
    /// cantinhos não aparecem para os outros). <paramref name="payoutsEnabled"/> = <c>false</c>:
    /// aprovado pelo admin, mas o Stripe ainda não liberou.
    /// </summary>
    private async Task<Guid> SaveHostAsync(Host host, bool payoutsEnabled = true)
    {
        host.LinkStripeAccount($"acct_test_{host.Id.Value:N}", DateTimeOffset.UtcNow);
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(payoutsEnabled, payoutsEnabled, payoutsEnabled, payoutsEnabled ? null : "requirements.past_due"), DateTimeOffset.UtcNow);

        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        hosts.Hosts.Add(host);
        await hosts.SaveChangesAsync(Ct);

        return host.Id.Value;
    }

    /// <summary>O Stripe bloqueou os repasses depois (ex.: pediu um documento novo).</summary>
    private async Task BlockPayoutsAsync(Guid hostId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        var host = await hosts.Hosts.FirstAsync(h => h.Id == new HostId(hostId), Ct);
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(true, false, false, "requirements.past_due"), DateTimeOffset.UtcNow);
        await hosts.SaveChangesAsync(Ct);
    }

    private async Task SuspendHostAsync(Guid hostId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        var host = await hosts.Hosts.FirstAsync(h => h.Id == new HostId(hostId), Ct);
        host.Suspend(DateTimeOffset.UtcNow);
        await hosts.SaveChangesAsync(Ct);
    }

    private async Task<Guid> CreateListingAsync(string token, object? listing = null)
    {
        var response = await SendAsync(HttpMethod.Post, ListingsRoute, token, listing ?? Cantinho());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<List<JsonElement>> ListMineAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, $"{ListingsRoute}/me", token);

        return [.. (await ReadAsync(response)).GetProperty("data").GetProperty("listings").EnumerateArray()];
    }

    private async Task<JsonElement> SearchAsync(string token, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"{ListingsRoute}?{query}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await ReadAsync(response)).GetProperty("data");
    }

    private async Task<List<Guid>> PhotoIdsAsync(string token, Guid listingId)
    {
        var response = await SendAsync(HttpMethod.Get, $"{ListingsRoute}/{listingId}", token);

        return [.. Photos(await ReadAsync(response)).Select(p => p.Id)];
    }

    private static object Cantinho(
        string neighborhood = "Zona 7",
        string? homeType = "house",
        int capacity = 3,
        decimal nightlyRate = 60.50m,
        string city = "Maringá",
        string[]? species = null,
        bool acceptsLarge = false,
        bool hasCats = false,
        bool hasKids = false) =>
        new
        {
            title = "Casa com quintal",
            description = "Casa térrea, quintal gramado e muito carinho.",
            homeType,
            hasYard = true,
            hasDogs = true,
            hasCats,
            hasKids,
            acceptedSpecies = species ?? ["cat", "dog"],
            acceptsSmall = true,
            acceptsMedium = true,
            acceptsLarge,
            capacity,
            nightlyRate,
            houseRules = "Nada de subir no sofá.",
            availableWeekdays = new[] { "saturday", "monday" },
            address = new
            {
                zipCode = "87020-000",
                street = "Rua das Flores",
                number = "120",
                neighborhood,
                city,
                state = "PR",
            },
        };

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> SendFileAsync(HttpMethod method, string route, string token, byte[] image)
    {
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var request = new HttpRequestMessage(method, route)
        {
            Content = new MultipartFormDataContent { { file, "file", "cantinho.png" } },
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    private static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];

    private static List<string?> Neighborhoods(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("neighborhood").GetString())];

    private static List<(Guid Id, int Position)> Photos(JsonElement body) =>
        [.. body.GetProperty("data").GetProperty("photos").EnumerateArray()
            .Select(p => (p.GetProperty("id").GetGuid(), p.GetProperty("position").GetInt32()))];
}
