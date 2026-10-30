using Microsoft.AspNetCore.SignalR;
using PetHost.Shared.Infrastructure.Http;

namespace PetHost.Shared.Infrastructure.Realtime;

/// <summary>
/// Identifica a conexão do SignalR pela claim <c>sub</c> do JWT (o id da conta). O provedor padrão
/// lê <c>NameIdentifier</c>, que não existe aqui: o host usa <c>MapInboundClaims = false</c> e as
/// claims chegam com o nome curto. Sem isto, <c>Clients.User(...)</c> não acharia ninguém.
/// </summary>
/// <remarks>
/// O id é o da <b>conta</b>: a outra conta da mesma pessoa (tutor × anfitrião) é outro usuário e
/// não recebe os eventos desta.
/// </remarks>
internal sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return connection.User?.FindFirst(ClaimsPrincipalExtensions.SubjectClaim)?.Value;
    }
}
