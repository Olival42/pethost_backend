using System.Globalization;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static PetHost.Modules.Reviews.IntegrationTests.ReviewsScenario;

namespace PetHost.Modules.Reviews.IntegrationTests.Reviews;

/// <summary>
/// Os limites próprios das avaliações numa instância da API com o rate limit ligado e baixo:
/// <c>review-writes</c> (avaliar, editar, apagar, responder) e <c>reports</c> (denunciar), por conta,
/// com <c>429</c> no envelope. Ler continua liberado.
/// </summary>
[Collection(ReviewsApiCollection.Name)]
public sealed class ReviewsRateLimitingTests(ReviewsApiFixture fixture) : IAsyncLifetime
{
    private const int WritesLimit = 2;
    private const int ReportsLimit = 1;

    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    private ReviewsScenario _scenario = null!;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        _api = fixture.WithSettings(new Dictionary<string, string>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:Global:PermitLimit"] = "1000",
            ["RateLimiting:Global:WindowSeconds"] = "60",
            ["RateLimiting:Policies:review-writes:PermitLimit"] = WritesLimit.ToString(CultureInfo.InvariantCulture),
            ["RateLimiting:Policies:review-writes:WindowSeconds"] = "60",
            ["RateLimiting:Policies:reports:PermitLimit"] = ReportsLimit.ToString(CultureInfo.InvariantCulture),
            ["RateLimiting:Policies:reports:WindowSeconds"] = "60",
        });
        _client = _api.CreateClient();
        _scenario = new ReviewsScenario(fixture, _client);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Writes_Should_Return429PerAccountAndLeaveReadingFree_When_TheAuthorGoesOverTheLimit()
    {
        // Arrange: avaliar e editar gastam as duas escritas da tutora
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var reviewId = Data(await ReadAsync(await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 4))).GetProperty("id").GetGuid();
        (await _scenario.SendAsync(HttpMethod.Patch, $"{ReviewsRoute}/{reviewId}", pair.OwnerToken, new { rating = 5 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var blocked = await _scenario.SendAsync(HttpMethod.Patch, $"{ReviewsRoute}/{reviewId}", pair.OwnerToken, new { rating = 3 });
        var hostReview = await _scenario.HostReviewAsync(pair, bookingId, 5);
        var ownerReads = await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/me", pair.OwnerToken);

        // Assert
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter!.Delta.Should().BePositive();
        Code(await ReadAsync(blocked)).Should().Be("TOO_MANY_REQUESTS");
        hostReview.StatusCode.Should().Be(HttpStatusCode.Created, "cada conta tem a própria contagem");
        ownerReads.StatusCode.Should().Be(HttpStatusCode.OK, "ler não conta no limite das escritas");
    }

    [Fact]
    public async Task Reports_Should_Return429_When_TheAccountGoesOverTheReportLimit()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var reviewId = Data(await ReadAsync(await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 1))).GetProperty("id").GetGuid();
        (await _scenario.HostReviewAsync(pair, bookingId, 5)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _scenario.SendAsync(HttpMethod.Post, $"{ReviewsRoute}/{reviewId}/report", pair.HostToken, new { reason = "offensive" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Act: a segunda denúncia nem chega ao domínio (seria 409)
        var blocked = await _scenario.SendAsync(HttpMethod.Post, $"{ReviewsRoute}/{reviewId}/report", pair.HostToken, new { reason = "spam" });

        // Assert
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        Code(await ReadAsync(blocked)).Should().Be("TOO_MANY_REQUESTS");
    }
}
