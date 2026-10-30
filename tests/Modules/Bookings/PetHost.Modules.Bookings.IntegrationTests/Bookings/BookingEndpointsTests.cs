using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Bookings.Application.Bookings.CloseOverdueStays;
using PetHost.Modules.Bookings.Application.Bookings.ExpireOverdueBookings;
using PetHost.Modules.Bookings.Application.Bookings.RemindDeadlines;
using PetHost.Modules.Bookings.Application.Bookings.RemindUpcomingStays;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Bookings;
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

    /// <summary>Primeira chegada que sempre dá para pedir agora (72h ou mais até a meia-noite do dia).</summary>
    private static DateOnly Arrival => Today.AddDays(4);

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
        booking.GetProperty("checkIn").GetString().Should().Be(Iso(Arrival));
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
        var acceptedBooking = Data(await ReadAsync(accepted));
        acceptedBooking.GetProperty("status").GetString().Should().Be("accepted");
        acceptedBooking.TryGetProperty("paymentDeadline", out _).Should().BeTrue();

        // A 1ª parcela paga (o módulo de pagamento confirma pelo contrato).
        (await PayAsync(bookingId)).Should().Be(BookingPaymentOutcome.Done);
        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken))).Should().Be("confirmed");

        // O dia da chegada.
        await ArriveTodayAsync(bookingId);
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
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["listingId", "petIds", "careTermsAccepted", "checkIn", "checkOut"]);
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
            careTermsAccepted = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["checkIn", "checkOut"]);
    }

    [Fact]
    public async Task Create_Should_ReturnAStableCodePerRule_When_BodyIsEmpty()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, new { });

        var body = await ReadAsync(response);
        ErrorCodesByField(body).Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["listingId"] = ["BOOKING_LISTING_ID_REQUIRED"],
            ["petIds"] = ["BOOKING_PET_IDS_REQUIRED"],
            ["careTermsAccepted"] = ["BOOKING_CARE_TERMS_NOT_ACCEPTED"],
            ["checkIn"] = ["BOOKING_CHECK_IN_REQUIRED"],
            ["checkOut"] = ["BOOKING_CHECK_OUT_REQUIRED"],
        });

        // errors[i] corresponde a messages[i]: mesma ordem, mesmo tamanho, e a mensagem continua lá.
        foreach (var detail in body.GetProperty("error").GetProperty("details").EnumerateArray())
            detail.GetProperty("errors").GetArrayLength().Should().Be(detail.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task Create_Should_ReturnTheCodeWithParams_When_TheStayStartsInLessThan72Hours()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId], Today.AddDays(2)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detail = (await ReadAsync(response)).GetProperty("error").GetProperty("details").EnumerateArray().Single();
        detail.GetProperty("field").GetString().Should().Be("checkIn");
        detail.GetProperty("messages")[0].GetString().Should().StartWith("Check-in must be at least 72 hours ahead");

        var error = detail.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("BOOKING_CHECK_IN_TOO_SOON");
        error.GetProperty("params").GetProperty("minimumHours").GetInt32().Should().Be(72);
        error.GetProperty("params").GetProperty("responseHours").GetInt32().Should().Be(24);
        error.GetProperty("params").GetProperty("acceptHours").GetInt32().Should().Be(48);
    }

    [Fact]
    public async Task Create_Should_Return400CheckInTooSoon_When_TheStayStartsInLessThan72Hours()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId], Today.AddDays(2)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("checkIn");
    }

    [Fact]
    public async Task Create_Should_Return422WeekdayNotAvailable_When_ListingIsClosedOnADayOfTheStay()
    {
        var closed = Arrival.AddDays(1).DayOfWeek.ToString().ToLowerInvariant();
        var stay = await ArrangeAsync(weekdays: [.. EveryDay.Where(day => day != closed)]);

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await ReadAsync(response);
        Code(body).Should().Be("BOOKING_WEEKDAY_NOT_AVAILABLE");
        ErrorParam(body, "date").GetString().Should().Be(Iso(Arrival.AddDays(1)), "the front reads the closed day from params, not from the sentence");
    }

    [Fact]
    public async Task Create_Should_Return422CapacityExceeded_When_AnAcceptedStayHoldsTheOnlySpot()
    {
        var stay = await ArrangeAsync(capacity: 1);
        var first = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{first}/acceptance", stay.HostToken);

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.ThorId]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await ReadAsync(response);
        Code(body).Should().Be("BOOKING_CAPACITY_EXCEEDED");
        ErrorParam(body, "date").GetString().Should().Be(Iso(Arrival));
    }

    [Fact]
    public async Task Create_Should_Return409_When_PetAlreadyHasABookingInThePeriod()
    {
        var stay = await ArrangeAsync();
        await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await ReadAsync(response);
        Code(body).Should().Be("BOOKING_PET_PERIOD_CONFLICT");
        ErrorParam(body, "petId").GetGuid().Should().Be(stay.PipocaId);
        body.GetProperty("error").GetProperty("params").EnumerateObject().Select(p => p.Name).Should().Equal("petId");
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
        await AcceptAndPayAsync(stay.HostToken, bookingId);
        await ArriveTodayAsync(bookingId);

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
        await AcceptAndPayAsync(stay.HostToken, bookingId);

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
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await AcceptAndPayAsync(stay.HostToken, bookingId);
        await MovePeriodAsync(bookingId, Today.AddDays(1), Today.AddDays(3));

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
        await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.ThorId, checkIn: Arrival.AddDays(5));
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
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId, checkIn: Arrival);
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

    [Fact]
    public async Task OverdueStays_Should_CloseTheNoShow_RemindTheHost_And_CheckOutAfterTheGrace()
    {
        var stay = await ArrangeAsync();

        // Paga e o pet nunca chegou: a estadia já acabou ontem.
        var noShow = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await AcceptAndPayAsync(stay.HostToken, noShow);
        await MovePeriodAsync(noShow, Today.AddDays(-3), Today.AddDays(-1));

        // Em andamento, hoje é o dia de saída: só o lembrete.
        var leavingToday = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.ThorId);
        await AcceptAndPayAsync(stay.HostToken, leavingToday);
        await ArriveTodayAsync(leavingToday);
        (await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{leavingToday}/check-in", stay.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await MovePeriodAsync(leavingToday, Today.AddDays(-2), Today);
        fixture.SentEmails.Clear();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var close = scope.ServiceProvider.GetRequiredService<ICommandHandler<CloseOverdueStaysCommand, CloseOverdueStaysResponse>>();
            (await close.HandleAsync(new CloseOverdueStaysCommand(), Ct)).Value.Should().Be(new CloseOverdueStaysResponse(1, 1, 0));
            (await close.HandleAsync(new CloseOverdueStaysCommand(), Ct)).Value.Should().Be(new CloseOverdueStaysResponse(0, 0, 0), "o lembrete vai uma vez só");
        }

        var closed = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{noShow}", stay.OwnerToken)));
        closed.GetProperty("status").GetString().Should().Be("cancelled");
        closed.GetProperty("cancelledBy").GetString().Should().Be("platform");
        closed.GetProperty("refundPercentage").GetInt32().Should().Be(50);
        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{leavingToday}", stay.HostToken))).Should().Be("in_progress");
        var reminder = await WaitForEmailAsync(e => e.Subject.Contains("Marque o check-out", StringComparison.Ordinal));
        reminder.ToAddress.Should().Be("cida@exemplo.com");

        // Três dias depois do dia de saída, sem check-out: a plataforma conclui.
        await MovePeriodAsync(leavingToday, Today.AddDays(-5), Today.AddDays(-Booking.AutoCheckOutAfterDays));

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var close = scope.ServiceProvider.GetRequiredService<ICommandHandler<CloseOverdueStaysCommand, CloseOverdueStaysResponse>>();
            (await close.HandleAsync(new CloseOverdueStaysCommand(), Ct)).Value.Should().Be(new CloseOverdueStaysResponse(0, 0, 1));
        }

        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{leavingToday}", stay.HostToken))).Should().Be("completed");
        (await WaitForEmailAsync(e => e.Subject.Contains("estadia concluída", StringComparison.Ordinal))).Should().NotBeNull();
    }

    // ----- a mesma pessoa dos dois lados -----

    [Fact]
    public async Task Create_Should_Return422_When_TheListingBelongsToTheOwnersOwnHostAccount()
    {
        // A Camila tem conta de tutora e, com o mesmo e-mail, de anfitriã com um cantinho.
        var stay = await ArrangeAsync();
        var ownHost = await RegisterHostAsync("camila@exemplo.com", "Camila Souza", "52998224725");
        var ownListing = await CreateListingAsync(ownHost, 3, EveryDay);

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(ownListing, [stay.PipocaId]));
        var conversation = await SendAsync(HttpMethod.Post, "/api/v1/conversations", stay.OwnerToken, new { listingId = ownListing });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_OWN_LISTING");
        conversation.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(conversation)).Should().Be("CONVERSATION_OWN_LISTING");
        (await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId]))).StatusCode
            .Should().Be(HttpStatusCode.Created, "o cantinho de outra pessoa continua liberado");
    }

    [Fact]
    public async Task Create_Should_RecordTheCareTerms_When_TheOwnerConfirms()
    {
        var stay = await ArrangeAsync();

        var created = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId]));
        var missing = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, new
        {
            listingId = stay.ListingId,
            petIds = new[] { stay.ThorId },
            checkIn = Iso(Arrival),
            checkOut = Iso(Arrival.AddDays(2)),
            careTermsAccepted = false,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        Data(await ReadAsync(created)).TryGetProperty("careTermsAcceptedAt", out _).Should().BeTrue();
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(missing)).Should().Equal("careTermsAccepted");
    }

    // ----- painel do anfitrião -----

    [Fact]
    public async Task Dashboard_Should_MoveAPaidStayWhoseArrivalPassedWithoutCheckIn_ToOverdueCheckIns()
    {
        var stay = await ArrangeAsync();
        var late = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await AcceptAndPayAsync(stay.HostToken, late);
        await MovePeriodAsync(late, Today.AddDays(-1), Today.AddDays(2));
        var today = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.ThorId, checkIn: Arrival.AddDays(5));
        await AcceptAndPayAsync(stay.HostToken, today);
        await MovePeriodAsync(today, Today, Today.AddDays(2));

        var dashboard = Data(await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/hosts/me/dashboard", stay.HostToken)));

        dashboard.GetProperty("overdueCheckIns").GetProperty("count").GetInt32().Should().Be(1);
        dashboard.GetProperty("overdueCheckIns").GetProperty("items")[0].GetProperty("bookingId").GetGuid().Should().Be(late);
        dashboard.GetProperty("upcomingStays").GetProperty("items").EnumerateArray()
            .Select(b => b.GetProperty("bookingId").GetGuid()).Should().Equal([today], "quem chega hoje ainda é próxima estadia");
    }

    // ----- lista do admin -----

    [Fact]
    public async Task AdminList_Should_FilterBookings_AndBeOnlyForTheAdmin()
    {
        var stay = await ArrangeAsync();
        var requested = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        var accepted = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.ThorId, checkIn: Arrival.AddDays(5));
        (await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{accepted}/acceptance", stay.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var admin = await AdminTokenAsync();

        var all = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}?listingId={stay.ListingId}", admin)));
        var onlyRequested = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}?status=requested&hostId={await HostIdAsync()}&ownerId={await OwnerIdAsync()}", admin)));
        var otherHost = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}?hostId={Guid.CreateVersion7()}", admin)));
        var badStatus = await SendAsync(HttpMethod.Get, $"{BookingsRoute}?status=unknown", admin);
        var asOwner = await SendAsync(HttpMethod.Get, BookingsRoute, stay.OwnerToken);

        all.GetProperty("totalCount").GetInt64().Should().Be(2);
        onlyRequested.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).Should().Equal(requested);
        onlyRequested.GetProperty("items")[0].TryGetProperty("contact", out _).Should().BeFalse();
        otherHost.GetProperty("totalCount").GetInt64().Should().Be(0);
        badStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        asOwner.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- lembretes de prazo -----

    [Fact]
    public async Task Deadlines_Should_RemindTheHostToAnswerAndThenTheOwnerToPay_OnceEach()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        // Pedido de 21h atrás: faltam 3h para o prazo de 24h.
        await fixture.ExecuteAsync($"UPDATE booking.bookings SET requested_at = now() - interval '21 hours' WHERE id = '{bookingId}'");
        fixture.SentEmails.Clear();
        (await RemindDeadlinesAsync()).Should().Be(new RemindDeadlinesResponse(1, 0));
        (await RemindDeadlinesAsync()).Should().Be(new RemindDeadlinesResponse(0, 0), "o lembrete vai uma vez só");
        var answer = await WaitForEmailAsync(e => e.Subject.Contains("espera a sua resposta", StringComparison.Ordinal));
        answer.ToAddress.Should().Be("cida@exemplo.com");
        answer.TextBody.Should().Contain("Camila Souza").And.Contain("Responda até");

        // Aceito há 19h: faltam 5h para o prazo de pagamento.
        (await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.ExecuteAsync($"UPDATE booking.bookings SET responded_at = now() - interval '19 hours' WHERE id = '{bookingId}'");
        (await RemindDeadlinesAsync()).Should().Be(new RemindDeadlinesResponse(0, 1));
        (await RemindDeadlinesAsync()).Should().Be(new RemindDeadlinesResponse(0, 0));
        var pay = await WaitForEmailAsync(e => e.Subject.Contains("falta pagar", StringComparison.Ordinal));
        pay.ToAddress.Should().Be("camila@exemplo.com");
        pay.TextBody.Should().Contain("Pague até");
    }

    private async Task<RemindDeadlinesResponse> RemindDeadlinesAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RemindDeadlinesCommand, RemindDeadlinesResponse>>();

        return (await handler.HandleAsync(new RemindDeadlinesCommand(), Ct)).Value!;
    }

    [Fact]
    public async Task UpcomingStays_Should_RemindBothSidesOnce_When_TheStayStartsTomorrow()
    {
        var stay = await ArrangeAsync();
        var tomorrow = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await AcceptAndPayAsync(stay.HostToken, tomorrow);
        await MovePeriodAsync(tomorrow, Today.AddDays(1), Today.AddDays(3));
        var later = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.ThorId, checkIn: Arrival.AddDays(5));
        await AcceptAndPayAsync(stay.HostToken, later);
        fixture.SentEmails.Clear();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var remind = scope.ServiceProvider.GetRequiredService<ICommandHandler<RemindUpcomingStaysCommand, int>>();
            (await remind.HandleAsync(new RemindUpcomingStaysCommand(), Ct)).Value.Should().Be(1);
            (await remind.HandleAsync(new RemindUpcomingStaysCommand(), Ct)).Value.Should().Be(0, "o lembrete vai uma vez só");
        }

        // O despachante roda em segundo plano: espera os dois lembretes.
        for (var attempt = 0; attempt < 100 && fixture.SentEmails.Count(e => e.Subject.Contains("começa amanhã", StringComparison.Ordinal)) < 2; attempt++)
            await Task.Delay(100, Ct);

        var reminders = fixture.SentEmails.Where(e => e.Subject.Contains("começa amanhã", StringComparison.Ordinal)).ToList();
        reminders.Should().HaveCount(2);
        reminders.Single(e => e.ToAddress == "camila@exemplo.com").TextBody.Should().Contain("Rua das Flores, 120").And.Contain("Telefone do anfitrião");
        reminders.Single(e => e.ToAddress == "cida@exemplo.com").TextBody.Should().Contain("Amanhã você recebe Pipoca").And.Contain("Telefone do tutor");
    }

    /// <summary>Simula o tempo passando: troca as datas da estadia direto no banco.</summary>
    private Task MovePeriodAsync(Guid bookingId, DateOnly checkIn, DateOnly checkOut) =>
        fixture.ExecuteAsync($"UPDATE booking.bookings SET check_in_date = '{Iso(checkIn)}', check_out_date = '{Iso(checkOut)}' WHERE id = '{bookingId}'");

    /// <summary>O pedido é feito com 72h de antecedência; aqui chega o dia: a estadia passa a começar hoje.</summary>
    private Task ArriveTodayAsync(Guid bookingId) => MovePeriodAsync(bookingId, Today, Today.AddDays(2));

    [Fact]
    public async Task Expiration_Should_ExpireAnAcceptedBooking_When_TheOwnerDoesNotPayIn24Hours()
    {
        var stay = await ArrangeAsync(capacity: 1);
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId, checkIn: Arrival);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);
        await fixture.ExecuteAsync($"UPDATE booking.bookings SET responded_at = now() - interval '25 hours' WHERE id = '{bookingId}'");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var expire = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpireOverdueBookingsCommand, ExpireOverdueBookingsResponse>>();
            (await expire.HandleAsync(new ExpireOverdueBookingsCommand(), Ct)).Value!.Expired.Should().Be(1);
        }

        var booking = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken)));
        booking.GetProperty("status").GetString().Should().Be("expired");

        // A vaga que o aceite segurava volta para a busca.
        var search = await SearchAsync(stay.OwnerToken, Arrival, Arrival.AddDays(2));
        search.GetProperty("totalCount").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Create_Should_Return422_When_TheOwnerHasAnOverdueInstallment()
    {
        var stay = await ArrangeAsync();
        await fixture.ExecuteAsync("""
            INSERT INTO payment.payments (id, booking_id, owner_id, host_id, amount_cents, currency, installment_count, plan_revision, status, created_at, updated_at)
            SELECT gen_random_uuid(), gen_random_uuid(), o.id, gen_random_uuid(), 20000, 'brl', 2, 1, 'active', now(), now() FROM owner.owners o;
            INSERT INTO payment.payment_installments (id, payment_id, number, amount_cents, due_at, status, intent_open, payment_attempts, refund_status, refunded_cents, payout_status, host_payout_cents, platform_fee_cents, reversed_cents, dispute_status)
            SELECT gen_random_uuid(), p.id, 2, 10000, now() - interval '3 days', 'overdue', false, 0, 'none', 0, 'held', 0, 0, 0, 'none' FROM payment.payments p;
            """);

        var response = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_OWNER_PAYMENT_OVERDUE");
    }

    // ----- contato depois da confirmação -----

    [Fact]
    public async Task Contact_Should_AppearOnlyAfterConfirmation_WithAddressForTheOwnerAndPhoneForBoth()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var beforeAccept = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken)));
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);
        var beforePayment = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken)));
        await PayAsync(bookingId);
        var ownerView = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken)));
        var hostView = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.HostToken)));

        beforeAccept.TryGetProperty("contact", out _).Should().BeFalse("antes da confirmação o tutor só vê o bairro");
        beforePayment.TryGetProperty("contact", out _).Should().BeFalse("aceita, mas ainda não paga");
        ownerView.GetProperty("contact").GetProperty("phone").GetString().Should().Be("44999990000");
        ownerView.GetProperty("contact").GetProperty("address").GetProperty("street").GetString().Should().Be("Rua das Flores");
        ownerView.GetProperty("contact").GetProperty("address").GetProperty("number").GetString().Should().Be("120");
        hostView.GetProperty("contact").GetProperty("phone").GetString().Should().Be("44999990000");
        hostView.GetProperty("contact").TryGetProperty("address", out _).Should().BeFalse();
    }

    // ----- e-mails -----

    [Fact]
    public async Task Emails_Should_TellTheOwnerAccepted_ThenBothConfirmedAfterPaying_ThenBothCancelled()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);
        var accepted = await WaitForEmailAsync(e => e.ToAddress == "camila@exemplo.com" && e.Subject.Contains("aceita", StringComparison.Ordinal));
        var confirmedTooEarly = BookingEmails().Any(e => e.Subject.Contains("confirmada", StringComparison.Ordinal) || e.Subject.Contains("garantida", StringComparison.Ordinal));

        await PayAsync(bookingId);
        await ArriveTodayAsync(bookingId);
        var hostConfirmed = await WaitForEmailAsync(e => e.ToAddress == "cida@exemplo.com" && e.Subject.Contains("confirmada", StringComparison.Ordinal));
        var ownerConfirmed = await WaitForEmailAsync(e => e.ToAddress == "camila@exemplo.com" && e.Subject.Contains("reserva garantida", StringComparison.Ordinal));

        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.OwnerToken, new { reason = "Mudei de planos" });
        var hostCancelled = await WaitForEmailAsync(e => e.ToAddress == "cida@exemplo.com" && e.Subject.Contains("cancelada", StringComparison.Ordinal));
        var ownerCancelled = await WaitForEmailAsync(e => e.ToAddress == "camila@exemplo.com" && e.Subject.Contains("cancelada", StringComparison.Ordinal));

        accepted.TextBody.Should().Contain("Pague até").And.NotContain("Rua das Flores", "o endereço só vai depois de pagar");
        confirmedTooEarly.Should().BeFalse("o anfitrião só é avisado quando o tutor paga");
        hostConfirmed.TextBody.Should().Contain("(44) 99999-0000");
        ownerConfirmed.TextBody.Should().Contain("Rua das Flores, 120").And.Contain("(44) 99999-0000");
        hostCancelled.TextBody.Should().Contain("cancelou a reserva").And.Contain("Mudei de planos");
        ownerCancelled.TextBody.Should().Contain("Reembolso: 50% do valor pago.");
    }

    /// <summary>Espera um e-mail específico (o despachante roda em segundo plano).</summary>
    private async Task<PetHost.Shared.Infrastructure.Email.EmailMessage> WaitForEmailAsync(Func<PetHost.Shared.Infrastructure.Email.EmailMessage, bool> match)
    {
        for (var attempt = 0; attempt < 100 && !fixture.SentEmails.Any(match); attempt++)
            await Task.Delay(100, Ct);

        return fixture.SentEmails.First(match);
    }

    [Fact]
    public async Task Emails_Should_TellTheHostAboutTheRequest_And_OnlyTheOwnerAboutTheRefusal()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var requested = await WaitForEmailAsync(e => e.Subject.Contains("pedido de reserva", StringComparison.Ordinal));
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/refusal", stay.HostToken, new { reason = "Vou viajar." });
        var emails = await WaitForBookingEmailsAsync(2);

        requested.ToAddress.Should().Be("cida@exemplo.com");
        requested.TextBody.Should().Contain("Camila Souza quer deixar o pet com você!")
            .And.Contain("Pets: Pipoca")
            .And.Contain("Responda até")
            .And.NotContain("99999-0000", "o telefone da tutora só sai depois do pagamento");
        emails.Should().HaveCount(2);
        var declined = emails.Single(e => e != requested);
        declined.ToAddress.Should().Be("camila@exemplo.com");
        declined.TextBody.Should().Contain("Motivo: Vou viajar.");
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

    // ----- inativar e suspender com reserva em aberto -----

    [Fact]
    public async Task OwnerDeactivation_Should_Return422_While_TheOwnerHasAnOpenBooking_And_WorkAfterCancelling()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var blocked = await SendAsync(HttpMethod.Post, "/api/v1/owners/me/deactivate", stay.OwnerToken);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.OwnerToken);
        var allowed = await SendAsync(HttpMethod.Post, "/api/v1/owners/me/deactivate", stay.OwnerToken);

        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(blocked)).Should().Be("OWNER_HAS_OPEN_BOOKING");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HostDeactivation_Should_Return422_While_AStayIsInProgress_And_WorkAfterCheckOut()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await AcceptAndPayAsync(stay.HostToken, bookingId);
        await ArriveTodayAsync(bookingId);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-in", stay.HostToken);

        var blocked = await SendAsync(HttpMethod.Post, "/api/v1/hosts/me/deactivate", stay.HostToken);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-out", stay.HostToken);
        var allowed = await SendAsync(HttpMethod.Post, "/api/v1/hosts/me/deactivate", stay.HostToken);

        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(blocked)).Should().Be("HOST_HAS_OPEN_BOOKING");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListingDeactivation_Should_Return422_While_ThereIsAnOpenBooking_And_WorkAfterItCloses()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);

        var blocked = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{stay.ListingId}/deactivation", stay.HostToken);
        var stillActive = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/v1/listings/{stay.ListingId}", stay.HostToken)));
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/refusal", stay.HostToken);
        var allowed = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{stay.ListingId}/deactivation", stay.HostToken);

        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(blocked)).Should().Be("LISTING_HAS_OPEN_BOOKING");
        stillActive.GetProperty("isActive").GetBoolean().Should().BeTrue();
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Accept_Should_Return422AndSendNoEmail_When_TheOwnerWasSuspendedAfterTheRequest()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await SuspendTheOwnerAsync();

        var response = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);
        var booking = await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.HostToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("BOOKING_OWNER_INACTIVE");
        Status(await ReadAsync(booking)).Should().Be("requested");
        await Task.Delay(500, Ct);
        BookingEmails().Should().NotContain(e => e.Subject.Contains("foi aceita!", StringComparison.Ordinal) || e.Subject.Contains("confirmada", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostSuspension_Should_CancelWhatHasNotStarted_And_LetTheAdminCheckOutTheStayInProgress()
    {
        var stay = await ArrangeAsync();
        var inProgress = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await AcceptAndPayAsync(stay.HostToken, inProgress);
        await ArriveTodayAsync(inProgress);
        (await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{inProgress}/check-in", stay.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.ThorId, Arrival.AddDays(5));
        await AcceptAndPayAsync(stay.HostToken, confirmed);
        var requested = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId, Arrival.AddDays(10));
        var admin = await AdminTokenAsync();
        fixture.SentEmails.Clear();

        var suspended = await SendAsync(HttpMethod.Post, $"/api/v1/hosts/{await HostIdAsync()}/suspension", admin, new { reason = "Documento de terceiro" });

        suspended.StatusCode.Should().Be(HttpStatusCode.OK);
        var paid = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{confirmed}", stay.OwnerToken)));
        paid.GetProperty("status").GetString().Should().Be("cancelled");
        paid.GetProperty("cancelledBy").GetString().Should().Be("platform");
        paid.GetProperty("refundPercentage").GetInt32().Should().Be(100);
        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{requested}", stay.OwnerToken))).Should().Be("cancelled");
        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{inProgress}", stay.OwnerToken))).Should().Be("in_progress");

        var cancelledEmails = await WaitForBookingEmailsAsync(4);
        cancelledEmails.Should().HaveCount(4).And.OnlyContain(e => e.TextBody.Contains("cancelada pela equipe do PetHost", StringComparison.Ordinal));
        cancelledEmails.Where(e => e.ToAddress == "camila@exemplo.com").Should().Contain(e => e.TextBody.Contains("Reembolso: 100% do valor pago.", StringComparison.Ordinal));

        // A anfitriã suspensa não entra mais: o admin encerra a estadia, com motivo.
        var noReason = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{inProgress}/check-out", admin);
        var checkedOut = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{inProgress}/check-out", admin, new { reason = "Tutora buscou a Pipoca" });

        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(noReason)).Should().Equal("reason");
        checkedOut.StatusCode.Should().Be(HttpStatusCode.OK);
        Status(await ReadAsync(checkedOut)).Should().Be("completed");
    }

    [Fact]
    public async Task OwnerSuspension_Should_CancelThePaidBookingWithoutRefund_When_AdminSuspendsTheOwner()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId, Arrival.AddDays(5));
        await AcceptAndPayAsync(stay.HostToken, bookingId);

        var suspended = await SendAsync(HttpMethod.Post, $"/api/v1/owners/{await OwnerIdAsync()}/suspension", await AdminTokenAsync(), new { reason = "Fraude de CPF" });
        var booking = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.HostToken)));

        suspended.StatusCode.Should().Be(HttpStatusCode.OK);
        booking.GetProperty("status").GetString().Should().Be("cancelled");
        booking.GetProperty("cancelledBy").GetString().Should().Be("platform");
        booking.GetProperty("refundPercentage").GetInt32().Should().Be(0);
        booking.GetProperty("cancellationReason").GetString().Should().Be(Booking.OwnerSuspendedReason);
    }

    [Fact]
    public async Task AdminCancellation_Should_RequireAReason_And_RefundInFull_When_TheStayStartsToday()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId);
        await AcceptAndPayAsync(stay.HostToken, bookingId);
        await ArriveTodayAsync(bookingId);
        var admin = await AdminTokenAsync();

        var noReason = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", admin);
        var cancelled = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", admin, new { reason = "Repasses da anfitriã bloqueados" });

        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        var booking = Data(await ReadAsync(cancelled));
        booking.GetProperty("cancelledBy").GetString().Should().Be("platform");
        booking.GetProperty("refundPercentage").GetInt32().Should().Be(100, "no dia da chegada o tutor levaria 50%; o admin devolve tudo");
    }

    // ----- agenda -----

    [Fact]
    public async Task Calendar_Should_BlockRequestsAndSearch_On_TheClosedDays_And_ReopenThem()
    {
        var stay = await ArrangeAsync();
        var from = Today.AddDays(10);

        var blocked = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{stay.ListingId}/availability-blocks", stay.HostToken,
            new { startDate = Iso(from.AddDays(1)), endDate = Iso(from.AddDays(3)), reason = "Viagem" });
        var overlapping = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{stay.ListingId}/availability-blocks", stay.HostToken,
            new { startDate = Iso(from.AddDays(3)), endDate = Iso(from.AddDays(4)) });
        var request = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId], from));
        var search = await SearchAsync(stay.OwnerToken, from, from.AddDays(2));
        var seenByOwner = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/v1/listings/{stay.ListingId}/availability-blocks", stay.OwnerToken)));

        blocked.StatusCode.Should().Be(HttpStatusCode.Created);
        var block = Data(await ReadAsync(blocked));
        block.GetProperty("days").GetInt32().Should().Be(3);
        block.GetProperty("reason").GetString().Should().Be("Viagem");
        overlapping.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(overlapping)).Should().Be("LISTING_AVAILABILITY_BLOCK_OVERLAP");
        request.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(request)).Should().Be("BOOKING_DATES_BLOCKED");
        search.GetProperty("totalCount").GetInt64().Should().Be(0);
        seenByOwner.GetArrayLength().Should().Be(1);
        seenByOwner[0].TryGetProperty("reason", out _).Should().BeFalse("o motivo é só da anfitriã");

        var reopened = await SendAsync(HttpMethod.Delete, $"/api/v1/listings/{stay.ListingId}/availability-blocks/{block.GetProperty("id").GetGuid()}", stay.HostToken);
        var retry = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.PipocaId], from));

        reopened.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(reopened)).GetArrayLength().Should().Be(0);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Calendar_Should_Return400WithEveryField_When_TheBodyIsInvalid()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{stay.ListingId}/availability-blocks", stay.HostToken,
            new { startDate = Iso(Today.AddDays(-1)), endDate = "31/12/2026" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(["startDate", "endDate"]);
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = BookingsApiFixture.AdminEmail, password = BookingsApiFixture.AdminPassword, role = "admin" },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await ReadAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> HostIdAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<HostsDbContext>().Hosts.Single().Id.Value;
    }

    private async Task<Guid> OwnerIdAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<OwnersDbContext>().Owners.Single().Id.Value;
    }

    /// <summary>Suspende a Camila direto em <c>owner.owners</c> (o aceite só olha o perfil de tutor).</summary>
    private async Task SuspendTheOwnerAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var owners = scope.ServiceProvider.GetRequiredService<OwnersDbContext>();
        owners.Owners.Single().Suspend(DateTimeOffset.UtcNow);
        await owners.SaveChangesAsync(Ct);
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

        var booked = await SearchAsync(stay.OwnerToken, Arrival, Arrival.AddDays(2));
        var later = await SearchAsync(stay.OwnerToken, Arrival.AddDays(2), Arrival.AddDays(4));

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

    // ----- "Combina com seu pet" (Listings, mas depende de Pets e das reservas) -----

    [Fact]
    public async Task Compatibility_Should_WarnAboutTheCatStayingInThePeriod_When_PetDoesNotGetAlongWithCats()
    {
        // Arrange: a Joana deixa a Mimi (gata) no período; a Pipoca não se dá com gatos.
        var stay = await ArrangeAsync();
        var joana = await RegisterOwnerAsync("joana@exemplo.com", "111.444.777-35", "Joana Lima");
        var mimi = await RegisterPetAsync(joana, "Mimi", size: "small", species: "cat");
        var booking = await CreateBookingAsync(joana, stay.ListingId, mimi);
        (await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{booking}/acceptance", stay.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await SendAsync(
            HttpMethod.Get,
            $"/api/v1/listings/{stay.ListingId}/compatibility?petId={stay.PipocaId}&checkIn={Iso(Arrival)}&checkOut={Iso(Arrival.AddDays(2))}",
            stay.OwnerToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Data(await ReadAsync(response));
        data.GetProperty("petName").GetString().Should().Be("Pipoca");
        data.GetProperty("matches").GetBoolean().Should().BeFalse();
        data.GetProperty("guests").EnumerateArray().Select(g => g.GetProperty("name").GetString()).Should().Equal("Mimi");
        var warning = data.GetProperty("warnings").EnumerateArray().Single();
        warning.GetProperty("code").GetString().Should().Be("guest_is_cat");
        warning.GetProperty("guest").GetProperty("species").GetString().Should().Be("cat");
    }

    [Fact]
    public async Task Compatibility_Should_MatchWithoutGuests_When_NoDatesAreSentAndTheHomeSuitsThePet()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/listings/{stay.ListingId}/compatibility?petId={stay.PipocaId}", stay.OwnerToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Data(await ReadAsync(response));
        data.GetProperty("matches").GetBoolean().Should().BeTrue();
        data.GetProperty("warnings").GetArrayLength().Should().Be(0);
        data.TryGetProperty("guests", out _).Should().BeFalse("sem datas não há hóspedes, e nulo é omitido");
    }

    [Fact]
    public async Task Compatibility_Should_Return404_When_PetIsFromAnotherOwner()
    {
        var stay = await ArrangeAsync();
        var joana = await RegisterOwnerAsync("joana@exemplo.com", "111.444.777-35", "Joana Lima");

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/listings/{stay.ListingId}/compatibility?petId={stay.PipocaId}", joana);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("LISTING_PET_NOT_FOUND");
    }

    [Fact]
    public async Task Compatibility_Should_Return400_When_PetIdIsMissing()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/listings/{stay.ListingId}/compatibility", stay.OwnerToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("petId");
    }

    [Fact]
    public async Task Compatibility_Should_Return403_When_CallerIsAHost()
    {
        var stay = await ArrangeAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/listings/{stay.ListingId}/compatibility?petId={stay.PipocaId}", stay.HostToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- helpers -----

    private async Task<JsonElement> SearchAsync(string token, DateOnly checkIn, DateOnly checkOut)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/listings?city=Maringá&checkIn={Iso(checkIn)}&checkOut={Iso(checkOut)}&pets=1", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await ReadAsync(response));
    }

    /// <summary>A 1ª parcela foi paga: o módulo de pagamento confirma a reserva pelo contrato público.</summary>
    private async Task<BookingPaymentOutcome> PayAsync(Guid bookingId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBookingPaymentOutcomes>().ConfirmPaidAsync(bookingId, Ct);
    }

    /// <summary>O anfitrião aceita e o tutor paga a 1ª parcela: a reserva fica confirmada.</summary>
    private async Task AcceptAndPayAsync(string hostToken, Guid bookingId)
    {
        var accepted = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", hostToken);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PayAsync(bookingId)).Should().Be(BookingPaymentOutcome.Done);
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

    /// <summary>A Dona Cida (ou outra pessoa): conta de anfitriã, verificada e com os repasses liberados pelo Stripe.</summary>
    private async Task<string> RegisterHostAsync(string email = "cida@exemplo.com", string fullName = "Dona Cida", string cpf = "12345678909")
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

        var data = Data(await ReadAsync(response));
        var userId = data.GetProperty("user").GetProperty("id").GetGuid();

        var host = Host.CreateIndividual(userId, Cpf.Create(cpf).Value!, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        host.LinkStripeAccount($"acct_test_{host.Id.Value:N}", DateTimeOffset.UtcNow);
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(true, true, true, null), DateTimeOffset.UtcNow);

        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        hosts.Hosts.Add(host);
        await hosts.SaveChangesAsync(Ct);

        return data.GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> RegisterPetAsync(string token, string name, string size = "medium", string species = "dog")
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/pets", token, new
        {
            species,
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

    /// <summary>Pedido de 2 noites a partir de <paramref name="checkIn"/> (padrão: <see cref="Arrival"/>).</summary>
    private static object Pedido(Guid listingId, Guid[] petIds, DateOnly? checkIn = null)
    {
        var from = checkIn ?? Arrival;

        return new { listingId, petIds, checkIn = Iso(from), checkOut = Iso(from.AddDays(2)), careTermsAccepted = true };
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

    /// <summary>Parâmetro do erro de topo (<c>error.params</c>).</summary>
    private static JsonElement ErrorParam(JsonElement body, string name) =>
        body.GetProperty("error").GetProperty("params").GetProperty(name);

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    /// <summary>Campo → códigos estáveis (<c>details[].errors[].code</c>), na ordem em que vieram.</summary>
    private static Dictionary<string, string[]> ErrorCodesByField(JsonElement body) =>
        body.GetProperty("error").GetProperty("details").EnumerateArray().ToDictionary(
            detail => detail.GetProperty("field").GetString()!,
            detail => detail.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("code").GetString()!).ToArray());

    private static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];
}
