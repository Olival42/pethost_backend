using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Uma conversa como a leitura projeta, antes de juntar os nomes dos dois lados.</summary>
/// <param name="OwnerId">Tutor (<c>owner.owners.id</c>).</param>
/// <param name="HostId">Anfitrião (<c>host.hosts.id</c>).</param>
/// <param name="LastMessage">A mensagem mais recente, ou <c>null</c> se ninguém escreveu.</param>
/// <param name="UnreadCount">Mensagens que quem vê recebeu e não leu.</param>
public sealed record ConversationRow(
    ConversationId Id,
    Guid OwnerId,
    Guid HostId,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt,
    MessageRow? LastMessage,
    int UnreadCount);
