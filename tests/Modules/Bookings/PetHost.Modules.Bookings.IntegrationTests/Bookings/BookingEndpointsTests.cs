using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Bookings.Application.Bookings.ExpireOverdueBookings;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Shared.Kernel.Messaging;
using Xunit;

namespace PetHost.Modules.Bookings.IntegrationTests.Bookings;

/// <summary>
/// Reservas ponta a ponta pela API: pedido com as validações (período, pets, dias da semana,
/// capacidade), aceite e recusa, check-in e check-out, cancelamento com reembolso, quem vê o quê
/// e expiração. O anfitrião é gravado direto em <c>host.hosts</c>, já verificado e com repasses
/// liberados; o resto passa pela API. As datas são relativas a hoje (horário de Brasília).
/// </summary>
[Collection(BookingsApiCollection.Name)]
public sealed class BookingEndpointsTests(BookingsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string BookingsRoute = "/api/v1/bookings";
    private const string Password = "Nova@Senha123";

    private static readonly string[] DogsAndCats = ["dog", "cat"];

    private static readonly string[] EveryDay = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => BookingClock.Today(DateTimeOffset.UtcNow);

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

    // ----- o caminho inteiro -----

    [Fact]
    public async Task Booking_Should_GoFromRequestToCompleted_When_HostAcceptsChecksInAndChecksOut()
    {
        var stay = await ArrangeAsync();

        // Pedido: 2 noites × R$ 60,50 × 1 pet.
        var created = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId]));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = Data(await ReadAsync(created));
        var bookingId = booking.GetProperty("id").GetGuid();
        created.Headers.Location!.ToString().Should().Be($"{BookingsRoute}/{bookingId}");
        booking.GetProperty("status").GetString().Should().Be("requested");
        booking.GetProperty("totalPrice").GetDecimal().Should().Be(121.00m);
        booking.GetProperty("nights").GetInt32().Should().Be(2);
        booking.GetProperty("checkIn").GetString().Should().Be(Iso(Today));
        booking.GetProperty("pets")[0].GetProperty("name").GetString().Should().Be("Pipoca");
        booking.GetProperty("listing").GetProperty("neighborhood").GetString().Should().Be("Zona 7");
        booking.GetProperty("host").GetProperty("name").GetString().Should().Be("Dona Cida");
        booking.TryGetProperty("responseDeadline", out _).Should().BeTrue();

        // O anfitrião vê o pedido em "minhas reservas".
        var mine = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/me?status=requested", stay.HostToken)));
        mine.GetProperty("totalCount").GetInt64().Should().Be(1);
        mine.GetProperty("items")[0].GetProperty("owner").GetProperty("name").GetString().Should().Be("Camila Souza");

        var accepted = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        Status(await ReadAsync(accepted)).Should().Be("confirmed");

        var checkedIn = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-in", stay.HostToken);
        checkedIn.StatusCode.Should().Be(HttpStatusCode.OK);
        Status(await ReadAsync(checkedIn)).Should().Be("in_progress");

        var lateCancel = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.OwnerToken);
        lateCancel.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(lateCancel)).Should().Be("BOOKING_CANNOT_CANCEL_AFTER_CHECK_IN");

        var checkedOut = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-out", stay.HostToken);
        checkedOut.StatusCode.Should().Be(HttpStatusCode.OK);
        Status(await ReadAsync(checkedOut)).Should().Be("completed");

        var seenByOwner = await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken);
        var final = Data(await ReadAsync(seenByOwner));
        final.GetProperty("status").GetString().Should().Be("completed");
        final.TryGetProperty("checkedInAt", out _).Should().BeTrue();
        final.TryGetProperty("checkedOutAt", out _).Should().BeTrue();
    }

    // ----- validações do pedido -----

    [Fact]
    public async Task Create_Should_Return400WithEveryField_When_BodyIsEmpty()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["listingId", "petIds", "checkIn", "checkOut"]);
    }

    [Fact]
    public async Task Create_Should_Return400_When_PeriodIsInvalid()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, new
        {
            listingId = stay.ListingId,
            petIds = new[] { stay.PipocaId },
            checkIn = Iso(Today.AddDays(-1)),
            checkOut = "25/12/2026",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["checkIn", "checkOut"]);
    }

    [Fact]
    public async Task Create_Should_Return422WeekdayNotAvailable_When_ListingIsClosedOnADayOfTheStay()
    {
        var closed = Today.AddDays(1).DayOfWeek.ToString().ToLowerInvariant();
        var stay = await ArrangeAsync(weekdays: [.. EveryDay.Where(day => day != closed)]);

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_WEEKDAY_NOT_AVAILABLE");
    }

    [Fact]
    public async Task Create_Should_Return422CapacityExceeded_When_ConfirmedStaysFillTheListing()
    {
        var stay = await ArrangeAsync(capacity: 1);
        var first = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{first}/acceptance", stay.HostToken);

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.ThorId]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_CAPACITY_EXCEEDED");
    }

    [Fact]
    public async Task Create_Should_Return409_When_PetAlreadyHasABookingInThePeriod()
    {
        var stay = await ArrangeAsync();
        await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await ReadAsync(response)).Should().Be("BOOKING_PET_PERIOD_CONFLICT");
    }

    [Fact]
    public async Task Create_Should_Return404PetNotFound_When_PetBelongsToAnotherOwner()
    {
        var stay = await ArrangeAsync();
        var other = await RegisterOwnerAsync("joana@exemplo.com", "111.444.777-35", "Joana Lima");
        var joanaPet = await RegisterPetAsync(other, "Mel");

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [joanaPet]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("BOOKING_PET_NOT_FOUND");
    }

    [Fact]
    public async Task Create_Should_Return422SizeNotAccepted_When_ListingDoesNotTakeLargeDogs()
    {
        var stay = await ArrangeAsync();
        var bob = await RegisterPetAsync(stay.OwnerToken, "Bob", size: "large");

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [bob]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_PET_SIZE_NOT_ACCEPTED");
    }

    [Fact]
    public async Task Create_Should_Return403_When_CallerIsAHost()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.HostToken, Pedido(stay.ListingId, [stay.PipocaId]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- aceite, recusa e capacidade -----

    [Fact]
    public async Task Acceptance_Should_LetOnlyOneOfTwoSimultaneousAcceptsTakeTheLastSpot()
    {
        var stay = await ArrangeAsync(capacity: 1);
        var pipoca = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        var thor = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.ThorId);

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Post, $"{BookingsRoute}/{pipoca}/acceptance", stay.HostToken),
            SendAsync(HttpMethod.Post, $"{BookingsRoute}/{thor}/acceptance", stay.HostToken));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity]);
    }

    [Fact]
    public async Task Refusal_Should_DeclineWithoutBody_And_AcceptAfterwardsShouldFail()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var declined = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/refusal", stay.HostToken);
        var accepted = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);

        declined.StatusCode.Should().Be(HttpStatusCode.OK);
        Status(await ReadAsync(declined)).Should().Be("declined");
        accepted.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(accepted)).Should().Be("BOOKING_NOT_AWAITING_RESPONSE");
    }

    [Fact]
    public async Task Acceptance_Should_Return403_When_CallerIsTheOwner()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var response = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.OwnerToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- cancelamento -----

    [Fact]
    public async Task Cancellation_Should_RefundHalf_When_OwnerCancelsAConfirmedStayOnTheDay()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);

        var response = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.OwnerToken, new { reason = "Mudei de planos" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var booking = Data(await ReadAsync(response));
        booking.GetProperty("status").GetString().Should().Be("cancelled");
        booking.GetProperty("cancelledBy").GetString().Should().Be("owner");
        booking.GetProperty("cancellationReason").GetString().Should().Be("Mudei de planos");
        booking.GetProperty("refundPercentage").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task Cancellation_Should_RefundEverything_When_HostCancels()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);

        var response = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.HostToken);

        var booking = Data(await ReadAsync(response));
        booking.GetProperty("cancelledBy").GetString().Should().Be("host");
        booking.GetProperty("refundPercentage").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task Cancellation_Should_Return422_When_CancelledTwice()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.OwnerToken);

        var response = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.OwnerToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_ALREADY_CANCELLED");
    }

    // ----- check-in e check-out -----

    [Fact]
    public async Task CheckIn_Should_Return422_When_BookingIsNotConfirmed()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var response = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-in", stay.HostToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_NOT_CONFIRMED");
    }

    [Fact]
    public async Task CheckIn_Should_Return422TooEarly_When_CheckInIsTomorrow()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId, checkIn: Today.AddDays(1));
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);

        var response = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-in", stay.HostToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_CHECK_IN_TOO_EARLY");
    }

    [Fact]
    public async Task CheckOut_Should_Return422_When_PetHasNotArrived()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);

        var response = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-out", stay.HostToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_NOT_IN_PROGRESS");
    }

    // ----- quem vê -----

    [Fact]
    public async Task GetById_Should_Return404_When_CallerIsAnotherOwner()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        var stranger = await RegisterOwnerAsync("joana@exemplo.com", "111.444.777-35", "Joana Lima");

        var response = await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stranger);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("BOOKING_NOT_FOUND");
    }

    [Fact]
    public async Task ListMine_Should_ReturnOnlyTheOwnersBookingsPaginated()
    {
        var stay = await ArrangeAsync();
        await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.ThorId, checkIn: Today.AddDays(5));
        var stranger = await RegisterOwnerAsync("joana@exemplo.com", "111.444.777-35", "Joana Lima");

        var mine = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/me?pageSize=1", stay.OwnerToken)));
        var theirs = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/me", stranger)));

        mine.GetProperty("totalCount").GetInt64().Should().Be(2);
        mine.GetProperty("totalPages").GetInt32().Should().Be(2);
        mine.GetProperty("items")[0].GetProperty("pets")[0].GetProperty("name").GetString().Should().Be("Thor", "a chegada mais recente vem primeiro");
        theirs.GetProperty("totalCount").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task ListMine_Should_Return400_When_StatusIsUnknown()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Get, $"{BookingsRoute}/me?status=paid", stay.OwnerToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("status");
    }

    // ----- expiração -----

    [Fact]
    public async Task Expiration_Should_CloseRequestsWithoutAnswerAfter24Hours()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId, checkIn: Today.AddDays(3));
        await fixture.ExecuteAsync($"UPDATE booking.bookings SET requested_at = now() - interval '25 hours' WHERE id = '{bookingId}'");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpireOverdueBookingsCommand, ExpireOverdueBookingsResponse>>();
            var result = await handler.HandleAsync(new ExpireOverdueBookingsCommand(), Ct);
            result.Value!.Expired.Should().Be(1);
        }

        var booking = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.HostToken)));
        booking.GetProperty("status").GetString().Should().Be("expired");
        booking.TryGetProperty("expiredAt", out _).Should().BeTrue();
    }

    // ----- contato depois da confirmação -----

    [Fact]
    public async Task Contact_Should_AppearOnlyAfterConfirmation_WithAddressForTheOwnerAndPhoneForBoth()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var beforeAccept = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken)));
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);
        var ownerView = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken)));
        var hostView = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.HostToken)));

        beforeAccept.TryGetProperty("contact", out _).Should().BeFalse("antes da confirmação o tutor só vê o bairro");
        ownerView.GetProperty("contact").GetProperty("phone").GetString().Should().Be("44999990000");
        ownerView.GetProperty("contact").GetProperty("address").GetProperty("street").GetString().Should().Be("Rua das Flores");
        ownerView.GetProperty("contact").GetProperty("address").GetProperty("number").GetString().Should().Be("120");
        hostView.GetProperty("contact").GetProperty("phone").GetString().Should().Be("44999990000");
        hostView.GetProperty("contact").TryGetProperty("address", out _).Should().BeFalse();
    }

    // ----- e-mails -----

    [Fact]
    public async Task Emails_Should_TellTheOwnerAcceptedAndTheHostConfirmed_ThenBothCancelled()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);
        var afterAccept = await WaitForBookingEmailsAsync(2);

        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.OwnerToken, new { reason = "Mudei de planos" });
        var all = await WaitForBookingEmailsAsync(4);

        afterAccept.Should().ContainSingle(e => e.ToAddress == "camila@exemplo.com" && e.Subject.Contains("aceita"));
        afterAccept.Should().ContainSingle(e => e.ToAddress == "cida@exemplo.com" && e.Subject.Contains("confirmada"));
        afterAccept.Single(e => e.ToAddress == "camila@exemplo.com").TextBody.Should().Contain("Rua das Flores, 120").And.Contain("(44) 99999-0000");

        var cancelled = all.Where(e => e.Subject.Contains("cancelada")).ToList();
        cancelled.Select(e => e.ToAddress).Should().BeEquivalentTo(["camila@exemplo.com", "cida@exemplo.com"]);
        cancelled.Single(e => e.ToAddress == "cida@exemplo.com").TextBody.Should().Contain("cancelou a reserva").And.Contain("Mudei de planos");
    }

    [Fact]
    public async Task Emails_Should_TellOnlyTheOwner_When_HostDeclines()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/refusal", stay.HostToken, new { reason = "Vou viajar." });
        var emails = await WaitForBookingEmailsAsync(1);

        emails.Should().ContainSingle();
        emails[0].ToAddress.Should().Be("camila@exemplo.com");
        emails[0].TextBody.Should().Contain("Motivo: Vou viajar.");
    }

    /// <summary>O despachante roda em segundo plano: espera os e-mails da reserva (assunto "PetHost — ... reserva ...").</summary>
    private async Task<List<PetHost.Shared.Infrastructure.Email.EmailMessage>> WaitForBookingEmailsAsync(int count)
    {
        for (var attempt = 0; attempt < 100 && BookingEmails().Count < count; attempt++)
            await Task.Delay(100, Ct);

        await Task.Delay(200, Ct);

        return BookingEmails();
    }

    private List<PetHost.Shared.Infrastructure.Email.EmailMessage> BookingEmails() =>
        [.. fixture.SentEmails.Where(e => e.Subject.Contains("reserva", StringComparison.Ordinal))];

    // ----- pets e reservas -----

    [Fact]
    public async Task PetDeactivation_Should_Return422_While_ThePetHasAnOpenBooking_And_WorkAfterCancelling()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var blocked = await SendAsync(HttpMethod.Post, $"/api/v1/pets/{stay.PipocaId}/deactivation", stay.OwnerToken);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.OwnerToken);
        var allowed = await SendAsync(HttpMethod.Post, $"/api/v1/pets/{stay.PipocaId}/deactivation", stay.OwnerToken);

        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(blocked)).Should().Be("PET_HAS_OPEN_BOOKING");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PetProfile_Should_BeVisibleToTheHost_OnlyAfterTheOwnerRequestsAStay()
    {
        var stay = await ArrangeAsync();

        var before = await SendAsync(HttpMethod.Get, $"/api/v1/pets/{stay.PipocaId}", stay.HostToken);
        await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        var after = await SendAsync(HttpMethod.Get, $"/api/v1/pets/{stay.PipocaId}", stay.HostToken);

        before.StatusCode.Should().Be(HttpStatusCode.NotFound);
        after.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(after)).GetProperty("name").GetString().Should().Be("Pipoca");
    }

    // ----- busca por datas, vaga e selos -----

    [Fact]
    public async Task Search_Should_HideAFullListingOnlyOnTheBookedDates_And_ShowBadges()
    {
        var stay = await ArrangeAsync(capacity: 1);
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);

        var booked = await SearchAsync(stay.OwnerToken, Today, Today.AddDays(2));
        var later = await SearchAsync(stay.OwnerToken, Today.AddDays(2), Today.AddDays(4));

        booked.GetProperty("totalCount").GetInt64().Should().Be(0, "a única vaga está ocupada nessas noites");
        later.GetProperty("totalCount").GetInt64().Should().Be(1, "quem sai no dia 2 libera a vaga para quem chega no dia 2");
        later.GetProperty("items")[0].GetProperty("badges").EnumerateArray().Select(b => b.GetString())
            .Should().Equal("neighbor", "fast_responder", "yard");
    }

    [Fact]
    public async Task Search_Should_Return400_When_OnlyCheckInIsSent()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/listings?city=Maringá&checkIn={Iso(Today)}", stay.OwnerToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("checkOut");
    }

    // ----- helpers -----

    private async Task<JsonElement> SearchAsync(string token, DateOnly checkIn, DateOnly checkOut)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/listings?city=Maringá&checkIn={Iso(checkIn)}&checkOut={Iso(checkOut)}&pets=1", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await ReadAsync(response));
    }

    private sealed record Stay(string OwnerToken, string HostToken, Guid ListingId, Guid PipocaId, Guid ThorId);

    /// <summary>A Camila (com a Pipoca, cachorro médio, e o Thor, pequeno) e o cantinho da Dona Cida.</summary>
    private async Task<Stay> ArrangeAsync(int capacity = 3, string[]? weekdays = null)
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25", "Camila Souza");
        var pipoca = await RegisterPetAsync(owner, "Pipoca");
        var thor = await RegisterPetAsync(owner, "Thor", size: "small");

        var host = await RegisterHostAsync();
        var listing = await CreateListingAsync(host, capacity, weekdays ?? EveryDay);

        return new Stay(owner, host, listing, pipoca, thor);
    }

    private async Task<string> RegisterOwnerAsync(string email, string cpf, string fullName)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/owners/register", new
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
    private async Task<string> RegisterHostAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
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

    private async Task<Guid> RegisterPetAsync(string token, string name, string size = "medium")
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/pets", token, new
        {
            species = "dog",
            name,
            breed = "SRD",
            size,
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

    private async Task<Guid> CreateListingAsync(string token, int capacity, string[] weekdays)
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
            capacity,
            nightlyRate = 60.50m,
            availableWeekdays = weekdays,
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBookingAsync(string token, Guid listingId, Guid petId, DateOnly? checkIn = null)
    {
        var response = await SendAsync(HttpMethod.Post, BookingsRoute, token, Pedido(listingId, [petId], checkIn));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Pedido de 2 noites a partir de <paramref name="checkIn"/> (padrão: hoje).</summary>
    private static object Pedido(Guid listingId, Guid[] petIds, DateOnly? checkIn = null)
    {
        var from = checkIn ?? Today;

        return new { listingId, petIds, checkIn = Iso(from), checkOut = Iso(from.AddDays(2)) };
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    private static JsonElement Data(JsonElement body) => body.GetProperty("data");

    private static string? Status(JsonElement body) => Data(body).GetProperty("status").GetString();

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    private static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];
}
