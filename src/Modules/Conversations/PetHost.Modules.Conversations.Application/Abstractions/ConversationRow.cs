using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Uma conversa como a leitura projeta, antes de juntar os nomes dos dois lados.</summary>
/// <param name="OwnerId">Tutor (<c>owner.owners.id</c>).</param>
/// <param name="HostId">Anfitrião (<c>host.hosts.id</c>).</param>
/// <param name="LastMessage">A mensagem mais recente, ou <c>null</c> se ninguém escreveu.</param>
/// <param name="UnreadCount">Mensagens que quem vê recebeu e não leu.</param>
/// <param name="Blocked">A conversa está bloqueada (por qualquer lado).</param>
/// <param name="BlockedByViewer">Quem vê foi quem bloqueou (só ele desbloqueia).</param>
/// <param name="BlockedByModeration">A moderação bloqueou (denúncia procedente); nenhum lado tira.</param>
public sealed record ConversationRow(
    ConversationId Id,
    Guid OwnerId,
    Guid HostId,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt,
    MessageRow? LastMessage,
    int UnreadCount,
    bool Blocked = false,
    bool BlockedByViewer = false,
    bool BlockedByModeration = false);
