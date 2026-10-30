using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PetHost.Modules.Bookings.Application.Bookings.AcceptBooking;
using PetHost.Modules.Bookings.Application.Bookings.CancelBooking;
using PetHost.Modules.Bookings.Application.Bookings.CheckInBooking;
using PetHost.Modules.Bookings.Application.Bookings.CheckOutBooking;
using PetHost.Modules.Bookings.Application.Bookings.CreateBooking;
using PetHost.Modules.Bookings.Application.Bookings.DeclineBooking;
using PetHost.Modules.Bookings.Application.Bookings.GetBookingById;
using PetHost.Modules.Bookings.Application.Bookings.ListBookings;
using PetHost.Modules.Bookings.Application.Bookings.ListMyBookings;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Bookings.Presentation.Bookings;

/// <summary>
/// Reservas: o tutor pede, o anfitrião aceita ou recusa, marca a chegada e a saída; os dois
/// podem cancelar até a chegada. Quem pede sai sempre do token — nunca do corpo.
/// </summary>
[ApiController]
[Route("api/v1/bookings")]
public sealed class BookingsController(
    ICommandHandler<CreateBookingCommand, BookingResponse> createBooking,
    ICommandHandler<AcceptBookingCommand, BookingResponse> acceptBooking,
    ICommandHandler<DeclineBookingCommand, BookingResponse> declineBooking,
    ICommandHandler<CancelBookingCommand, BookingResponse> cancelBooking,
    ICommandHandler<CheckInBookingCommand, BookingResponse> checkInBooking,
    ICommandHandler<CheckOutBookingCommand, BookingResponse> checkOutBooking,
    IQueryHandler<GetBookingByIdQuery, BookingResponse> getBookingById,
    IQueryHandler<ListMyBookingsQuery, PagedResult<BookingResponse>> listMyBookings,
    IQueryHandler<ListBookingsQuery, PagedResult<BookingResponse>> listBookings) : ControllerBase
{
    /// <summary>O tutor pede uma estadia num cantinho para um ou mais pets dele.</summary>
    /// <remarks>
    /// Corpo: <c>listingId</c>, <c>petIds</c> (1 a 10, sem repetição), <c>checkIn</c> e
    /// <c>checkOut</c> (<c>"2026-12-20"</c>) e <c>careTermsAccepted: true</c> (o tutor leva a ração e
    /// entrega os remédios — escopo, regra 8). Período: chegada a partir de hoje (horário de
    /// Brasília) e até 365 dias à frente, saída depois da chegada, até 30 noites — erros de formato
    /// voltam juntos num só 400. Nasce <c>requested</c>; o anfitrião tem 24h para responder. Valor
    /// = diária × noites × pets.
    /// <para>
    /// Cantinho inexistente ou fora da busca: 404 <c>BOOKING_LISTING_NOT_FOUND</c>. Pet que não é
    /// do tutor: 404 <c>BOOKING_PET_NOT_FOUND</c>. Pet desativado: 422 <c>BOOKING_PET_INACTIVE</c>.
    /// Espécie ou porte não aceitos: 422 <c>BOOKING_PET_SPECIES_NOT_ACCEPTED</c> /
    /// <c>BOOKING_PET_SIZE_NOT_ACCEPTED</c>. Pet já em outra reserva no período: 409
    /// <c>BOOKING_PET_PERIOD_CONFLICT</c>. Algum dia do período fora dos dias em que o cantinho
    /// recebe: 422 <c>BOOKING_WEEKDAY_NOT_AVAILABLE</c>. Sem vaga em alguma noite: 422
    /// <c>BOOKING_CAPACITY_EXCEEDED</c>. Tutor com parcela em atraso: 422 <c>BOOKING_OWNER_PAYMENT_OVERDUE</c>.
    /// Cantinho da própria pessoa (conta de anfitrião com o mesmo e-mail): 422 <c>BOOKING_OWN_LISTING</c>.
    /// </para>
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = Roles.Owner)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateBookingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await createBooking.HandleAsync(
            new CreateBookingCommand(User.GetUserId(), request.ListingId, request.PetIds, request.CheckIn, request.CheckOut, request.CareTermsAccepted),
            cancellationToken);

        return result.IsSuccess
            ? result.ToCreatedResult($"/api/v1/bookings/{result.Value!.Id}")
            : result.ToActionResult();
    }

    /// <summary>As reservas da conta logada: do tutor, as que ele pediu; do anfitrião, as dos cantinhos dele.</summary>
    /// <remarks>
    /// Filtro opcional <c>status</c> (repetido ou separado por vírgula): <c>requested</c>,
    /// <c>accepted</c>, <c>confirmed</c>, <c>in_progress</c>, <c>completed</c>, <c>declined</c>,
    /// <c>expired</c>, <c>cancelled</c>. Da chegada mais recente para a mais antiga. Paginado:
    /// <c>page</c> a partir de 1, <c>pageSize</c> até 100.
    /// </remarks>
    [HttpGet("me")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<PagedResult<BookingResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<BookingResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<BookingResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<BookingResponse>>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PagedResult<BookingResponse>>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMineAsync([FromQuery] MyBookingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await listMyBookings.HandleAsync(
            new ListMyBookingsQuery(User.GetUserId(), CurrentRole(), request.Status, request.Page, request.PageSize),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Todas as reservas, para o admin, com filtros que se somam.</summary>
    /// <remarks>
    /// Filtros opcionais: <c>status</c> (repetido ou separado por vírgula, mesmos valores de
    /// <c>/bookings/me</c>), <c>ownerId</c> (id do tutor), <c>hostId</c> (id do anfitrião) e
    /// <c>listingId</c>. Sem filtro, todas. Da chegada mais recente para a mais antiga; sem o contato
    /// de nenhum lado (como o detalhe para o admin). Paginado: <c>page</c> a partir de 1,
    /// <c>pageSize</c> até 100.
    /// </remarks>
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<PagedResult<BookingResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<BookingResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<BookingResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<BookingResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync([FromQuery] BookingsAdminRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await listBookings.HandleAsync(
            new ListBookingsQuery(request.Status, request.OwnerId, request.HostId, request.ListingId, request.Page, request.PageSize),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Uma reserva. Só o tutor que pediu, o anfitrião do cantinho e o admin.</summary>
    /// <remarks>Reserva de outra pessoa: 404 <c>BOOKING_NOT_FOUND</c>, como se não existisse.</remarks>
    [HttpGet("{bookingId:guid}")]
    [Authorize]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await getBookingById.HandleAsync(new GetBookingByIdQuery(User.GetUserId(), CurrentRole(), bookingId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O anfitrião aceita o pedido.</summary>
    /// <remarks>
    /// Só <c>requested</c> e dentro de 24h do pedido (e antes do dia de chegada passar). Vai para
    /// <c>accepted</c>: segura a vaga e o tutor tem 24h para pagar a 1ª parcela (<c>paymentDeadline</c>);
    /// quem confirma é o pagamento (<c>/bookings/{id}/payment</c>). Sem vaga
    /// em alguma noite: 422 <c>BOOKING_CAPACITY_EXCEEDED</c>. Prazo vencido: a reserva expira e
    /// volta 422 <c>BOOKING_RESPONSE_EXPIRED</c>. Outro status: 422 <c>BOOKING_NOT_AWAITING_RESPONSE</c>.
    /// Tutor suspenso ou inativo desde o pedido: 422 <c>BOOKING_OWNER_INACTIVE</c> (recuse ou deixe expirar).
    /// </remarks>
    [HttpPost("{bookingId:guid}/acceptance")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AcceptAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await acceptBooking.HandleAsync(new AcceptBookingCommand(User.GetUserId(), bookingId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O anfitrião recusa o pedido, com um motivo opcional.</summary>
    /// <remarks>
    /// Só <c>requested</c> e dentro do prazo. Vai para <c>declined</c>. Prazo vencido: 422
    /// <c>BOOKING_RESPONSE_EXPIRED</c>.
    /// </remarks>
    [HttpPost("{bookingId:guid}/refusal")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeclineAsync(
        Guid bookingId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingReasonRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await declineBooking.HandleAsync(
            new DeclineBookingCommand(User.GetUserId(), bookingId, request?.Reason),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O tutor, o anfitrião ou o admin cancela a reserva.</summary>
    /// <remarks>
    /// Vale até o check-in (<c>requested</c>, <c>accepted</c> ou <c>confirmed</c>); depois que o pet
    /// chegou: 422 <c>BOOKING_CANNOT_CANCEL_AFTER_CHECK_IN</c>. Reserva confirmada (paga) leva o
    /// reembolso em <c>refundPercentage</c>, sobre o que já foi pago: 100 se o anfitrião ou o admin
    /// cancela ou se o tutor cancela com 48h ou mais de antecedência da chegada; 50 depois disso. O
    /// pagamento devolve o dinheiro e cancela as parcelas em aberto. Motivo opcional para tutor e
    /// anfitrião; o <b>admin</b> cancela qualquer reserva (<c>cancelledBy = platform</c>) e o motivo é
    /// obrigatório (vai para a trilha e para o e-mail dos dois lados). Já cancelada: 422
    /// <c>BOOKING_ALREADY_CANCELLED</c>; recusada ou expirada: 422 <c>BOOKING_ALREADY_CLOSED</c>.
    /// </remarks>
    [HttpPost("{bookingId:guid}/cancellation")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host},{Roles.Admin}")]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelAsync(
        Guid bookingId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingReasonRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await cancelBooking.HandleAsync(
            new CancelBookingCommand(User.GetUserId(), CurrentRole(), bookingId, request?.Reason),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O anfitrião marca "chegou": o pet está com ele.</summary>
    /// <remarks>
    /// Só <c>confirmed</c> (senão 422 <c>BOOKING_NOT_CONFIRMED</c>), a partir do dia de chegada
    /// (senão 422 <c>BOOKING_CHECK_IN_TOO_EARLY</c>) e antes do dia de saída (senão 422
    /// <c>BOOKING_CHECK_IN_WINDOW_CLOSED</c>). Vai para <c>in_progress</c>.
    /// </remarks>
    [HttpPost("{bookingId:guid}/check-in")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CheckInAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await checkInBooking.HandleAsync(new CheckInBookingCommand(User.GetUserId(), bookingId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O anfitrião (ou o admin) marca "voltou pra casa": o pet está de novo com o tutor.</summary>
    /// <remarks>
    /// Só <c>in_progress</c> (senão 422 <c>BOOKING_NOT_IN_PROGRESS</c>); pode ser antes do dia de
    /// saída (o tutor buscou mais cedo). Vai para <c>completed</c> e libera o repasse. O <b>admin</b>
    /// encerra a estadia de qualquer reserva — o anfitrião suspenso não entra mais — com motivo
    /// obrigatório no corpo (<c>{ "reason": "..." }</c>, vai para a trilha); para o anfitrião o corpo
    /// é ignorado.
    /// </remarks>
    [HttpPost("{bookingId:guid}/check-out")]
    [Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<BookingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CheckOutAsync(
        Guid bookingId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingReasonRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await checkOutBooking.HandleAsync(
            new CheckOutBookingCommand(User.GetUserId(), bookingId, CurrentRole(), request?.Reason),
            cancellationToken);

        return result.ToActionResult();
    }

    private string CurrentRole() =>
        User.IsInRole(Roles.Admin) ? Roles.Admin
        : User.IsInRole(Roles.Host) ? Roles.Host
        : Roles.Owner;
}
