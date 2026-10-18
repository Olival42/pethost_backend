using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Reviews.Application.Reminders.SendReviewReminders;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Modules.Reviews.Infrastructure.Notifications;
using PetHost.Modules.Reviews.Infrastructure.Persistence;
using PetHost.Shared.Kernel.Messaging;
using Xunit;
using static PetHost.Modules.Reviews.IntegrationTests.ReviewsScenario;

namespace PetHost.Modules.Reviews.IntegrationTests.Reviews;

/// <summary>
/// A segunda fase das avaliações de ponta a ponta: o anfitrião avalia o tutor e os pets, a resposta
/// do avaliado, a denúncia e a moderação do admin, a nota e o selo na busca do Listings, e os
/// e-mails (avaliação recebida, lembrete, aviso à moderação).
/// </summary>
[Collection(ReviewsApiCollection.Name)]
public sealed class ReviewsBothSidesTests(ReviewsApiFixture fixture) : IAsyncLifetime
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
    public async Task HostReview_Should_BeVisibleOnlyToHostsAndTheOwner_When_TheHostReviewsTheOwnerAndThePet()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var petId = (await _scenario.BookingPetIdsAsync(pair, bookingId)).Single();

        // Act: a anfitriã avalia a Camila e a Pipoca
        var created = await _scenario.HostReviewAsync(pair, bookingId, 5, [new { petId, rating = 4, comment = "Latiu à noite." }]);

        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var review = Data(await ReadAsync(created));
        var reviewId = review.GetProperty("id").GetGuid();
        var ownerId = review.GetProperty("ownerId").GetGuid();
        review.GetProperty("authorSide").GetString().Should().Be("host");
        review.GetProperty("author").GetProperty("name").GetString().Should().Be("Dona");
        var pet = review.GetProperty("pets")[0];
        pet.GetProperty("petId").GetGuid().Should().Be(petId);
        pet.GetProperty("name").GetString().Should().Be("Pipoca");
        pet.GetProperty("rating").GetInt32().Should().Be(4);

        // Os dois lados avaliam a mesma reserva, cada um uma vez.
        (await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5)).StatusCode.Should().Be(HttpStatusCode.Created);
        var again = await _scenario.HostReviewAsync(pair, bookingId, 3);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(again)).Should().Be("REVIEW_ALREADY_EXISTS");

        // A avaliação do tutor não é pública: sem login, 404; outro tutor, 404.
        (await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", token: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var stranger = await _scenario.RegisterOwnerAsync("bruno@exemplo.com", "111.444.777-35", "Bruno Lima");
        (await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", stranger)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // O tutor avaliado vê, nas que recebeu; os anfitriões veem pela rota do tutor.
        var received = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/me/received", pair.OwnerToken)));
        received.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(reviewId);

        var forHosts = await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/owners/{ownerId}/reviews", pair.HostToken);
        forHosts.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(forHosts)).GetProperty("totalCount").GetInt64().Should().Be(1);

        var summary = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/owners/{ownerId}/reviews/summary", pair.HostToken)));
        summary.GetProperty("average").GetDecimal().Should().Be(5.0m);
        summary.GetProperty("count").GetInt32().Should().Be(1);

        (await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/owners/{ownerId}/reviews", pair.OwnerToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A Camila recebeu o e-mail de avaliação recebida, sem o texto.
        var email = await fixture.WaitForEmailAsync(e => e.Subject == ReviewEmailNotifier.ReceivedSubject && e.ToAddress == "camila@exemplo.com", EmailTimeout);
        email.Should().NotBeNull();
        email!.TextBody.Should().Contain("nota 5").And.NotContain("Tutora pontual");
    }

    [Fact]
    public async Task HostReview_Should_Return400_When_ThePetIsNotFromTheStay()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);

        // Act
        var response = await _scenario.HostReviewAsync(pair, bookingId, 5, [new { petId = Guid.CreateVersion7(), rating = 4 }]);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("pets");
    }

    [Fact]
    public async Task Reply_Should_ShowPublicly_When_TheHostAnswersTheOwnersReview()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var reviewId = Data(await ReadAsync(await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 4, "Boa casa."))).GetProperty("id").GetGuid();

        // Às cegas: antes de publicar, a anfitriã nem lê a avaliação.
        (await _scenario.SendAsync(HttpMethod.Put, $"{ReviewsRoute}/{reviewId}/reply", pair.HostToken, new { text = "Oi" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _scenario.HostReviewAsync(pair, bookingId, 5)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var replied = await _scenario.SendAsync(HttpMethod.Put, $"{ReviewsRoute}/{reviewId}/reply", pair.HostToken, new { text = "Obrigada, Camila!" });

        // Assert
        replied.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicReview = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", token: null)));
        publicReview.GetProperty("reply").GetProperty("text").GetString().Should().Be("Obrigada, Camila!");

        var byAuthor = await _scenario.SendAsync(HttpMethod.Put, $"{ReviewsRoute}/{reviewId}/reply", pair.OwnerToken, new { text = "Eu mesma" });
        byAuthor.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var removed = await _scenario.SendAsync(HttpMethod.Delete, $"{ReviewsRoute}/{reviewId}/reply", pair.HostToken);
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(removed)).TryGetProperty("reply", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Moderation_Should_HideAndShowTheReview_When_TheAdminUpholdsAReportAndThenUndoesIt()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        var hostId = await _scenario.HostIdAsync(pair);
        var reviewId = Data(await ReadAsync(await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 1, "Texto ofensivo."))).GetProperty("id").GetGuid();
        (await _scenario.HostReviewAsync(pair, bookingId, 5)).StatusCode.Should().Be(HttpStatusCode.Created);
        var admin = await _scenario.AdminTokenAsync();

        // Act: a anfitriã denuncia
        var reported = await _scenario.SendAsync(HttpMethod.Post, $"{ReviewsRoute}/{reviewId}/report", pair.HostToken, new { reason = "offensive" });

        // Assert
        reported.StatusCode.Should().Be(HttpStatusCode.Created);
        var reportId = Data(await ReadAsync(reported)).GetProperty("id").GetGuid();
        reported.Headers.Location!.ToString().Should().Be($"{ReviewsRoute}/reports/{reportId}");

        var twice = await _scenario.SendAsync(HttpMethod.Post, $"{ReviewsRoute}/{reviewId}/report", pair.HostToken, new { reason = "spam" });
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(twice)).Should().Be("REVIEW_REPORT_ALREADY_EXISTS");

        var own = await _scenario.SendAsync(HttpMethod.Post, $"{ReviewsRoute}/{reviewId}/report", pair.OwnerToken, new { reason = "spam" });
        own.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await fixture.WaitForEmailAsync(e => e.Subject == ReviewEmailNotifier.ModerationSubject && e.ToAddress == ReviewsApiFixture.ModerationAlertEmail, EmailTimeout))
            .Should().NotBeNull();

        // A fila do admin (só admin).
        (await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/reports", pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var queue = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/reports", admin)));
        queue.GetProperty("items")[0].GetProperty("review").GetProperty("id").GetGuid().Should().Be(reviewId);

        // Procedente: some da lista, da leitura pública e da nota; o autor vê o motivo.
        var resolved = await _scenario.SendAsync(HttpMethod.Put, $"{ReviewsRoute}/reports/{reportId}/resolution", admin, new { outcome = "upheld", note = "Linguagem ofensiva." });
        resolved.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(resolved)).GetProperty("status").GetString().Should().Be("upheld");

        (await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", token: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}?hostId={hostId}", token: null))).GetProperty("totalCount").GetInt64().Should().Be(0);
        Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/summary?hostId={hostId}", token: null))).GetProperty("count").GetInt32().Should().Be(0);

        var mine = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/me", pair.OwnerToken)));
        mine.GetProperty("items")[0].GetProperty("moderation").GetProperty("reason").GetString().Should().Be("Linguagem ofensiva.");

        var edit = await _scenario.SendAsync(HttpMethod.Patch, $"{ReviewsRoute}/{reviewId}", pair.OwnerToken, new { rating = 2 });
        edit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(edit)).Should().Be("REVIEW_HIDDEN_BY_MODERATION");

        var again = await _scenario.SendAsync(HttpMethod.Put, $"{ReviewsRoute}/reports/{reportId}/resolution", admin, new { outcome = "dismissed", note = "Ok." });
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // O admin desfaz: a avaliação volta.
        var unhidden = await _scenario.SendAsync(HttpMethod.Delete, $"{ReviewsRoute}/{reviewId}/moderation-hide", admin);
        unhidden.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/{reviewId}", token: null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_Should_ShowTheRatingAndTheWellRatedBadge_When_TheHostHasThreeGoodReviews()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var hostId = await _scenario.HostIdAsync(pair);
        await SeedOwnerReviewsAsync(hostId, pair.ListingId, 5, 5, 4);

        // Act
        var search = await _scenario.SendAsync(HttpMethod.Get, "/api/v1/listings?city=Maringá", pair.OwnerToken);
        var detail = await _scenario.SendAsync(HttpMethod.Get, $"/api/v1/listings/{pair.ListingId}", pair.OwnerToken);
        var summary = await _scenario.SendAsync(HttpMethod.Get, $"{ReviewsRoute}/summary?listingId={pair.ListingId}", token: null);

        // Assert
        search.StatusCode.Should().Be(HttpStatusCode.OK);
        var card = Data(await ReadAsync(search)).GetProperty("items")[0];
        card.GetProperty("rating").GetProperty("average").GetDecimal().Should().Be(4.7m);
        card.GetProperty("rating").GetProperty("count").GetInt32().Should().Be(3);
        card.GetProperty("badges").EnumerateArray().Select(b => b.GetString()).Should().Contain("well_rated");

        var listing = Data(await ReadAsync(detail));
        listing.GetProperty("rating").GetProperty("count").GetInt32().Should().Be(3);
        listing.GetProperty("badges").EnumerateArray().Select(b => b.GetString()).Should().Contain("well_rated");

        Data(await ReadAsync(summary)).GetProperty("average").GetDecimal().Should().Be(4.7m);
    }

    [Fact]
    public async Task Search_Should_LeaveTheRatingOut_When_NobodyReviewed()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();

        // Act
        var search = await _scenario.SendAsync(HttpMethod.Get, "/api/v1/listings?city=Maringá", pair.OwnerToken);

        // Assert
        var card = Data(await ReadAsync(search)).GetProperty("items")[0];
        card.TryGetProperty("rating", out _).Should().BeFalse();
        card.GetProperty("badges").EnumerateArray().Select(b => b.GetString()).Should().NotContain("well_rated");
    }

    [Fact]
    public async Task Reminders_Should_EmailBothSidesOnce_When_NobodyReviewedADayAfterCheckOut()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        await fixture.ExecuteAsync($"UPDATE booking.bookings SET checked_out_at = now() - interval '2 days' WHERE id = '{bookingId}'");

        // Act
        var first = await RunRemindersAsync();
        var second = await RunRemindersAsync();

        // Assert
        first.Should().Be(2);
        second.Should().Be(0);
        (await fixture.WaitForEmailAsync(e => e.Subject == ReviewEmailNotifier.ReminderSubject && e.ToAddress == "camila@exemplo.com", EmailTimeout)).Should().NotBeNull();
        (await fixture.WaitForEmailAsync(e => e.Subject == ReviewEmailNotifier.ReminderSubject && e.ToAddress == "cida@exemplo.com", EmailTimeout)).Should().NotBeNull();

        await using var scope = fixture.Services.CreateAsyncScope();
        var reminders = await scope.ServiceProvider.GetRequiredService<ReviewsDbContext>().ReviewReminders.AsNoTracking().CountAsync(Ct);
        reminders.Should().Be(2);
    }

    [Fact]
    public async Task Reminders_Should_SkipTheSideThatReviewed_When_OneSideAlreadyReviewed()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var bookingId = await _scenario.CompleteStayAsync(pair);
        (await _scenario.ReviewAsync(pair.OwnerToken, bookingId, 5)).StatusCode.Should().Be(HttpStatusCode.Created);
        await fixture.ExecuteAsync($"UPDATE booking.bookings SET checked_out_at = now() - interval '2 days' WHERE id = '{bookingId}'");

        // Act
        var sent = await RunRemindersAsync();

        // Assert
        sent.Should().Be(1);
    }

    private async Task<int> RunRemindersAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SendReviewRemindersCommand, SendReviewRemindersResponse>>();

        var result = await handler.HandleAsync(new SendReviewRemindersCommand(DelayHours: 24, BatchSize: 100), Ct);
        result.IsSuccess.Should().BeTrue();

        return result.Value!.EmailsSent;
    }

    /// <summary>Avaliações de tutores gravadas direto (uma reserva concluída cada), para a média.</summary>
    private async Task SeedOwnerReviewsAsync(Guid hostId, Guid listingId, params int[] ratings)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        var now = DateTimeOffset.UtcNow;

        foreach (var rating in ratings)
        {
            var ownerId = Guid.CreateVersion7();
            var stay = new ReviewedStay(Guid.CreateVersion7(), ownerId, hostId, listingId, IsCompleted: true, now.AddDays(-1), []);
            var review = Review.Create(stay, ReviewParty.Owner, ownerId, rating, null, null, now).Value!;
            review.Publish(now);
            db.Reviews.Add(review);
        }

        await db.SaveChangesAsync(Ct);
    }
}
