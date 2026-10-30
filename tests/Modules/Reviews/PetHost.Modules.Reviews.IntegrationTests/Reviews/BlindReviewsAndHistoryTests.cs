using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Reviews.Application.Reviews.NotifyPublishedReviews;
using PetHost.Modules.Reviews.Infrastructure.Notifications;
using PetHost.Shared.Kernel.Messaging;
using Xunit;
using static PetHost.Modules.Reviews.IntegrationTests.ReviewsScenario;

namespace PetHost.Modules.Reviews.IntegrationTests.Reviews;

/// <summary>
/// A terceira fase de ponta a ponta: a avaliação às cegas (publicada pelos dois lados ou no fim do
/// prazo, com o aviso da tarefa), o histórico de notas do pet e a nota do tutor no pedido de reserva.
/// </summary>
[Collection(ReviewsApiCollection.Name)]
public sealed class BlindReviewsAndHistoryTests(ReviewsApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan EmailTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _client = fixture.CreateClient();
    private ReviewsScenario _scenario = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
    public async Task BlindReview_Should_BePublishedAndNotifiedByTheTask_When_TheWindowEndsWithOnlyOneSide()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var reviewId = Data(await ReadAsync(await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 4, "Boa casa."))).GetProperty("id").GetGuid();

        (await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", token: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RunNoticesAsync()).Should().Be(0);

        // Act: o prazo acaba sem a anfitriã avaliar
        await fixture.ExecuteAsync($"UPDATE review.reviews SET published_at = now() - interval '1 minute' WHERE id = '{reviewId}'");
        var notified = await RunNoticesAsync();

        // Assert
        notified.Should().Be(1);
        (await RunNoticesAsync()).Should().Be(0);
        (await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", token: null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var email = await fixture.WaitForEmailAsync(e => e.Subject == ReviewEmailNotifier.ReceivedSubject && e.ToAddress == "cida@exemplo.com", EmailTimeout);
        email.Should().NotBeNull();
        email!.TextBody.Should().Contain("nota 4").And.NotContain("Boa casa");
    }

    [Fact]
    public async Task PetHistory_Should_ShowThePetRatings_When_TheHostRatedThePetAndBothReviewsArePublished()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var petId = (await _scenario.BookingPetIdsAsync(pair, bookingId)).Single();
        (await _scenario.HostReviewAsync(pair, bookingId, 5, [new { petId, rating = 4, comment = "Latiu à noite." }])).StatusCode.Should().Be(HttpStatusCode.Created);

        // Às cegas: antes do tutor avaliar, o histórico ainda não mostra.
        Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/pets/{petId}/reviews", pair.HostToken))).GetProperty("totalCount").GetInt64().Should().Be(0);

        (await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var history = await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/pets/{petId}/reviews", pair.HostToken);
        var summary = await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/pets/{petId}/reviews/summary", pair.HostToken);

        // Assert
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = Data(await ReadAsync(history)).GetProperty("items")[0];
        item.GetProperty("rating").GetInt32().Should().Be(4);
        item.GetProperty("comment").GetString().Should().Be("Latiu à noite.");
        item.GetProperty("host").GetProperty("name").GetString().Should().Be("Dona");

        var rating = Data(await ReadAsync(summary));
        rating.GetProperty("average").GetDecimal().Should().Be(4.0m);
        rating.GetProperty("count").GetInt32().Should().Be(1);

        (await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/pets/{petId}/reviews", pair.OwnerToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Booking_Should_ShowTheOwnerRatingToTheHostOnly_When_TheOwnerWasReviewed()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        (await _scenario.HostReviewAsync(pair, bookingId, 4)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var byHost = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", pair.HostToken)));
        var byOwner = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/bookings/{bookingId}", pair.OwnerToken)));

        // Assert
        byHost.GetProperty("ownerRating").GetProperty("average").GetDecimal().Should().Be(4.0m);
        byHost.GetProperty("ownerRating").GetProperty("count").GetInt32().Should().Be(1);
        byOwner.TryGetProperty("ownerRating", out _).Should().BeFalse();
    }

    private async Task<int> RunNoticesAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<NotifyPublishedReviewsCommand, NotifyPublishedReviewsResponse>>();

        var result = await handler.HandleAsync(new NotifyPublishedReviewsCommand(BatchSize: 100), Ct);
        result.IsSuccess.Should().BeTrue();

        return result.Value!.Notified;
    }
}
