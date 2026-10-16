using PetHost.Modules.Conversations.Application.Messages.Responses;

namespace PetHost.Modules.Conversations.Application.Conversations.Responses;

/// <summary>Uma conversa vista por um dos dois lados.</summary>
/// <param name="OwnerId">Tutor (<c>owner.owners.id</c>).</param>
/// <param name="Owner">Nome e foto do tutor. Omitido se a conta sumiu.</param>
/// <param name="HostId">Anfitrião (<c>host.hosts.id</c>).</param>
/// <param name="Host">Nome (empresa pelo nome fantasia) e foto do anfitrião. Omitido se a conta sumiu.</param>
/// <param name="LastMessage">A mensagem mais recente. Omitida enquanto ninguém escreveu.</param>
/// <param name="UnreadCount">Mensagens que quem vê recebeu e ainda não leu.</param>
/// <param name="Blocked">Bloqueada por um dos lados: ninguém envia, os dois leem.</param>
/// <param name="BlockedByMe">Quem vê bloqueou (e pode desbloquear o próprio bloqueio).</param>
/// <param name="BlockedByModeration">A moderação bloqueou (denúncia procedente): ninguém desbloqueia pelo app.</param>
public sealed record ConversationResponse(
    Guid Id,
    Guid OwnerId,
    PartySummary? Owner,
    Guid HostId,
    PartySummary? Host,
    MessageResponse? LastMessage,
    int UnreadCount,
    bool Blocked,
    bool BlockedByMe,
    bool BlockedByModeration,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt);
