using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PetHost.Shared.Contracts.Authorization;

namespace PetHost.Modules.Conversations.Presentation.Realtime;

/// <summary>
/// Hub do chat em <see cref="Route"/>. Só <b>empurra</b> eventos para o cliente
/// (<see cref="ConversationEvents"/>): não tem método que o cliente chame. Enviar mensagem, abrir
/// conversa e marcar como lida são rotas REST de <c>/api/v1/conversations</c>.
/// </summary>
/// <remarks>
/// Autenticado com o mesmo JWT da API, que o cliente manda na query (<c>access_token</c>). Cada
/// conexão é da conta do token (claim <c>sub</c>). A conexão é fechada quando o token expira
/// (<c>CloseOnAuthenticationExpiration</c>); o cliente reconecta com o token renovado.
/// </remarks>
[Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
public sealed class ConversationsHub : Hub
{
    /// <summary>Endereço do hub.</summary>
    public const string Route = "/hubs/conversations";
}
