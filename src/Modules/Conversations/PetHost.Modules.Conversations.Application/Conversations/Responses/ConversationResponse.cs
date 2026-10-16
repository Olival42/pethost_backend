using PetHost.Modules.Conversations.Application.Messages.Responses;

namespace PetHost.Modules.Conversations.Application.Conversations.Responses;

/// <summary>Uma conversa vista por um dos dois lados.</summary>
/// <param name="OwnerId">Tutor (<c>owner.owners.id</c>).</param>
/// <param name="Owner">Nome e foto do tutor. Omitido se a conta sumiu.</param>
/// <param name="HostId">Anfitrião (<c>host.hosts.id</c>).</param>
/// <param name="Host">Nome (empresa pelo nome fantasia) e foto do anfitrião. Omitido se a conta sumiu.</param>
/// <param name="LastMessage">A mensagem mais recente. Omitida enquanto ninguém escreveu.</param>
/// <param name="UnreadCount">Mensagens que quem vê recebeu e ainda não leu.</param>
public sealed record ConversationResponse(
    Guid Id,
    Guid OwnerId,
    PartySummary? Owner,
    Guid HostId,
    PartySummary? Host,
    MessageResponse? LastMessage,
    int UnreadCount,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt);
