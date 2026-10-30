using System.Net;
using FluentAssertions;
using Xunit;
using static PetHost.Modules.Reviews.IntegrationTests.ReviewsScenario;

namespace PetHost.Modules.Reviews.IntegrationTests.Reviews;

/// <summary>
/// As rotas de <c>/api/v1/reviews</c> de ponta a ponta: a reserva lida do Bookings pelo contrato
/// público, a gravação em <c>review.reviews</c>, a leitura anônima, a autorização e o envelope.
/// </summary>
[Collection(ReviewsApiCollection.Name)]
public sealed class ReviewsControllerTests(ReviewsApiFixture fixture) : IAsyncLifetime
{
    private readonly HttpClient _client = fixture.CreateClient();
    private ReviewsScenario _scenario = null!;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        _scenario = new ReviewsScenario(fixture, _client);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Reviews_Should_FollowTheWholeLifecycle_When_BothSidesReviewACompletedStay()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);

        // Act: avalia
        var created = await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5, "  A Pipoca voltou feliz!  ");

        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync(created);
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        var review = Data(body);
        var reviewId = review.GetProperty("id").GetGuid();
        var hostId = review.GetProperty("hostId").GetGuid();
        created.Headers.Location!.ToString().Should().Be($"{ReviewsRoute}/{reviewId}");
        review.GetProperty("listingId").GetGuid().Should().Be(pair.ListingId);
        review.GetProperty("bookingId").GetGuid().Should().Be(bookingId);
        review.GetProperty("rating").GetInt32().Should().Be(5);
        review.GetProperty("comment").GetString().Should().Be("A Pipoca voltou feliz!");
        review.GetProperty("author").GetProperty("name").GetString().Should().Be("Camila");
        review.GetProperty("authorSide").GetString().Should().Be("owner");
        review.TryGetProperty("ownerId", out _).Should().BeFalse();
        review.TryGetProperty("editableUntil", out _).Should().BeTrue();
        review.TryGetProperty("publishedAt", out _).Should().BeTrue();

        // Às cegas: enquanto a anfitriã não avalia, ninguém além da autora lê.
        (await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", token: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}?hostId={hostId}", token: null))).GetProperty("totalCount").GetInt64().Should().Be(0);

        // Act & Assert: edita antes da publicação (PATCH parcial; "" apaga o comentário)
        var patched = await _scenario.SendAsync(HttpMethod.Patch, $"{ReviewsRoute}/{reviewId}", pair.OwnerToken, new { rating = 4, comment = "" });
        patched.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = Data(await ReadAsync(patched));
        updated.GetProperty("rating").GetInt32().Should().Be(4);
        updated.TryGetProperty("comment", out _).Should().BeFalse();

        // A anfitriã avalia: as duas são publicadas na hora.
        (await _scenario.HostReviewAsync(pair, bookingId, 5)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act & Assert: leitura pública, sem token, sem a reserva
        var publicOne = await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", token: null);
        publicOne.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicReview = Data(await ReadAsync(publicOne));
        publicReview.TryGetProperty("bookingId", out _).Should().BeFalse();
        publicReview.TryGetProperty("editableUntil", out _).Should().BeFalse();

        var byHost = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}?hostId={hostId}", token: null)));
        byHost.GetProperty("totalCount").GetInt64().Should().Be(1);
        byHost.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(reviewId);

        var byListing = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}?listingId={pair.ListingId}&page=1&pageSize=5", token: null)));
        byListing.GetProperty("totalCount").GetInt64().Should().Be(1);
        byListing.GetProperty("pageSize").GetInt32().Should().Be(5);

        var otherHost = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}?hostId={Guid.CreateVersion7()}", token: null)));
        otherHost.GetProperty("totalCount").GetInt64().Should().Be(0);

        // Act & Assert: minhas avaliações, com a reserva
        var mine = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/me", pair.OwnerToken)));
        mine.GetProperty("items")[0].GetProperty("bookingId").GetGuid().Should().Be(bookingId);

        // Publicada, não muda mais.
        var late = await _scenario.SendAsync(HttpMethod.Patch, $"{ReviewsRoute}/{reviewId}", pair.OwnerToken, new { rating = 1 });
        late.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(late)).Should().Be("REVIEW_ALREADY_PUBLISHED");

        // Act & Assert: apaga; some da leitura e a reserva não pode ser avaliada de novo
        var deleted = await _scenario.SendAsync(HttpMethod.Delete, $"{ReviewsRoute}/{reviewId}", pair.OwnerToken);
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(deleted)).GetProperty("success").GetBoolean().Should().BeTrue();

        var gone = await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", token: null);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(gone)).Should().Be("REVIEW_NOT_FOUND");

        var again = await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(again)).Should().Be("REVIEW_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Create_Should_Return409_When_TheBookingIsReviewedTwice()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        (await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var response = await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 3);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("REVIEW_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Create_Should_Return422_When_TheStayHasNotEndedYet()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.StartStayAsync(pair);

        // Act
        var response = await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("REVIEW_BOOKING_NOT_COMPLETED");
    }

    [Fact]
    public async Task Create_Should_Return422_When_TheCheckOutWasMoreThanSevenDaysAgo()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        await fixture.ExecuteAsync($"UPDATE booking.bookings SET checked_out_at = now() - interval '8 days' WHERE id = '{bookingId}'");

        // Act
        var response = await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("REVIEW_WINDOW_CLOSED");
    }

    [Fact]
    public async Task Create_Should_Return404_When_TheBookingBelongsToAnotherOwner()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var stranger = await _scenario.RegisterOwnerAsync("bruno@exemplo.com", "111.444.777-35", "Bruno Lima");

        // Act
        var response = await _scenario.ReviewAsync(stranger, bookingId, 1);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("REVIEW_BOOKING_NOT_FOUND");
    }

    [Fact]
    public async Task Create_Should_Return400WithTheFields_When_ThePayloadIsInvalid()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Post, ReviewsRoute, pair.OwnerToken, new { bookingId = Guid.Empty, rating = 6, comment = new string('a', 1001) });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadAsync(response);
        Code(body).Should().Be("VALIDATION_ERROR");
        Fields(body).Should().BeEquivalentTo(["bookingId", "rating", "comment"]);
    }

    [Fact]
    public async Task Create_Should_Return401_When_ThereIsNoToken()
    {
        // Act
        var response = await _scenario.ReviewAsync(token: null!, Guid.CreateVersion7(), 5);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Code(await ReadAsync(response)).Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task Create_Should_Return403_When_TheTokenIsAnAdmin()
    {
        // Arrange
        var admin = await _scenario.AdminTokenAsync();

        // Act
        var response = await _scenario.ReviewAsync(admin, Guid.CreateVersion7(), 5);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(response)).Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task UpdateAndDelete_Should_Return404_When_TheReviewBelongsToAnotherOwner()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var reviewId = Data(await ReadAsync(await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5))).GetProperty("id").GetGuid();
        var stranger = await _scenario.RegisterOwnerAsync("bruno@exemplo.com", "111.444.777-35", "Bruno Lima");

        // Act
        var patched = await _scenario.SendAsync(HttpMethod.Patch, $"{ReviewsRoute}/{reviewId}", stranger, new { rating = 1 });
        var deleted = await _scenario.SendAsync(HttpMethod.Delete, $"{ReviewsRoute}/{reviewId}", stranger);

        // Assert
        patched.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(patched)).Should().Be("REVIEW_NOT_FOUND");
        deleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var still = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", pair.OwnerToken)));
        still.GetProperty("rating").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task Update_Should_Return422_When_TheEditWindowIsOver()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var reviewId = Data(await ReadAsync(await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5))).GetProperty("id").GetGuid();
        await fixture.ExecuteAsync($"UPDATE review.reviews SET stay_ended_at = now() - interval '8 days' WHERE id = '{reviewId}'");

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Patch, $"{ReviewsRoute}/{reviewId}", pair.OwnerToken, new { rating = 1 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("REVIEW_WINDOW_CLOSED");

        var deleted = await _scenario.SendAsync(HttpMethod.Delete, $"{ReviewsRoute}/{reviewId}", pair.OwnerToken);
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_Should_Return400WithTheFields_When_PagingIsInvalid()
    {
        // Act
        var response = await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}?page=0&pageSize=500", token: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["page", "pageSize"]);
    }

    [Fact]
    public async Task ListMine_Should_Return401_When_ThereIsNoToken()
    {
        // Act
        var response = await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/me", token: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
