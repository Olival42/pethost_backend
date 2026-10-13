using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Bookings.Application.Bookings.CreateBooking;

/// <summary>
/// O tutor pede uma estadia: um cantinho, um período e um ou mais pets dele. Nasce
/// <c>requested</c>; o anfitrião tem 24h para aceitar ou recusar.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="PetIds">Pets do tutor, de 1 a 10, sem repetição.</param>
/// <param name="CheckIn">
/// Dia de chegada, <c>yyyy-MM-dd</c> (a partir de hoje, no horário de Brasília). Texto, como as
/// outras datas da API: data mal formada volta como erro do campo, junto com os outros.
/// </param>
/// <param name="CheckOut">Dia de saída, <c>yyyy-MM-dd</c> (depois da chegada, até 30 noites).</param>
public sealed record CreateBookingCommand(
    Guid UserId,
    Guid ListingId,
    IReadOnlyList<Guid>? PetIds,
    string? CheckIn,
    string? CheckOut) : ICommand<BookingResponse>;
