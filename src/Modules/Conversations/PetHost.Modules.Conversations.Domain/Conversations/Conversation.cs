using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Domain.Conversations;

/// <summary>
/// A conversa entre um tutor e um anfitrião: uma por dupla, que serve antes, durante e depois das
/// reservas. Espelha a tabela <c>conversation.conversations</c>.
/// </summary>
/// <remarks>
/// Guarda ids de outros módulos como FK lógica: o tutor (<c>owner.owners.id</c>) e o anfitrião
/// (<c>host.hosts.id</c>) — o id do perfil, nunca o da conta. As mensagens são outro agregado
/// (<c>Message</c>): a conversa não carrega o histórico, só o instante da última mensagem.
/// </remarks>
public sealed class Conversation : Entity<ConversationId>
{
    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Conversation()
    {
    }

    private Conversation(Guid ownerId, Guid hostId, DateTimeOffset now)
        : base(ConversationId.New())
    {
        OwnerId = ownerId;
        HostId = hostId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Tutor da conversa (<c>owner.owners.id</c>).</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Anfitrião da conversa (<c>host.hosts.id</c>).</summary>
    public Guid HostId { get; private set; }

    /// <summary>Quando chegou a última mensagem. Nulo enquanto ninguém escreveu.</summary>
    public DateTimeOffset? LastMessageAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Qual lado bloqueou a conversa. Nulo enquanto nenhum dos dois bloqueou.</summary>
    public ConversationParty? BlockedBy { get; private set; }

    /// <summary>Quando um dos lados bloqueou. Nulo enquanto nenhum dos dois bloqueou.</summary>
    public DateTimeOffset? BlockedAt { get; private set; }

    /// <summary>
    /// Quando a moderação bloqueou (denúncia procedente). Independe do bloqueio dos lados: nenhum dos
    /// dois tira. Nulo enquanto a moderação não bloqueou.
    /// </summary>
    public DateTimeOffset? ModerationBlockedAt { get; private set; }

    /// <summary>A moderação bloqueou a conversa.</summary>
    public bool IsBlockedByModeration => ModerationBlockedAt is not null;

    /// <summary>A conversa está bloqueada (por um dos lados ou pela moderação): ninguém envia.</summary>
    public bool IsBlocked => BlockedBy is not null || IsBlockedByModeration;

    /// <summary>
    /// Quando o tutor recebeu o último e-mail de "mensagens não lidas" desta conversa. Gravado pela
    /// tarefa do aviso (<c>IConversationRepository.MarkUnreadNotifiedAsync</c>), não pelo domínio.
    /// </summary>
    public DateTimeOffset? OwnerUnreadNotifiedAt { get; private set; }

    /// <summary>Quando o anfitrião recebeu o último e-mail de "mensagens não lidas" desta conversa.</summary>
    public DateTimeOffset? HostUnreadNotifiedAt { get; private set; }

    /// <summary>Última atividade: a última mensagem ou, sem nenhuma, a abertura. Base da retenção.</summary>
    public DateTimeOffset LastActivityAt => LastMessageAt ?? CreatedAt;

    /// <summary>
    /// O tutor puxa conversa com o anfitrião. Os dois ids são de perfis que o módulo já conferiu;
    /// id vazio é bug de quem chama.
    /// </summary>
    public static Conversation Start(Guid ownerId, Guid hostId, DateTimeOffset now)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException("The owner id cannot be empty.", nameof(ownerId));

        if (hostId == Guid.Empty)
            throw new ArgumentException("The host id cannot be empty.", nameof(hostId));

        return new Conversation(ownerId, hostId, now);
    }

    /// <summary>O perfil <paramref name="profileId"/> é o lado <paramref name="party"/> desta conversa?</summary>
    public bool HasParticipant(ConversationParty party, Guid profileId) =>
        party == ConversationParty.Owner ? OwnerId == profileId : HostId == profileId;

    /// <summary>
    /// Registra uma mensagem nova. O instante só avança: uma mensagem gravada fora de ordem não
    /// faz a conversa parecer mais antiga.
    /// </summary>
    public void RecordMessage(DateTimeOffset sentAt)
    {
        if (LastMessageAt is null || sentAt > LastMessageAt)
            LastMessageAt = sentAt;

        UpdatedAt = sentAt > UpdatedAt ? sentAt : UpdatedAt;
    }

    /// <summary>Sem mensagem nova desde <paramref name="cutoff"/> (nem abertura, se ninguém escreveu).</summary>
    public bool IsInactiveSince(DateTimeOffset cutoff) => LastActivityAt < cutoff;

    /// <summary>
    /// Bloqueia a conversa pelo lado <paramref name="party"/>: enquanto bloqueada, ninguém envia, e os
    /// dois continuam lendo o histórico. Devolve se mudou (bloquear de novo é idempotente). Já
    /// bloqueada pelo outro lado: <c>CONVERSATION_ALREADY_BLOCKED</c>.
    /// </summary>
    public Result<bool> Block(ConversationParty party, DateTimeOffset now)
    {
        if (BlockedBy == party)
            return Result<bool>.Success(false);

        if (BlockedBy is not null)
            return Result<bool>.Failure(ConversationsErrors.AlreadyBlocked);

        BlockedBy = party;
        BlockedAt = now;
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// A moderação bloqueia a conversa (o admin julgou procedente uma denúncia dela). Ninguém envia, os
    /// dois continuam lendo, e nenhum dos lados tira: o desbloqueio de um lado só tira o bloqueio dele.
    /// Devolve se mudou (já bloqueada pela moderação é idempotente).
    /// </summary>
    public bool BlockByModeration(DateTimeOffset now)
    {
        if (IsBlockedByModeration)
            return false;

        ModerationBlockedAt = now;
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;

        return true;
    }

    /// <summary>
    /// O admin tira o bloqueio da moderação (procedente dado por engano, ou reconsiderado). O bloqueio
    /// de um dos lados, se houver, continua. Devolve se mudou (sem bloqueio da moderação é idempotente).
    /// </summary>
    public bool LiftModerationBlock(DateTimeOffset now)
    {
        if (!IsBlockedByModeration)
            return false;

        ModerationBlockedAt = null;
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;

        return true;
    }

    /// <summary>
    /// Desbloqueia o bloqueio de um dos lados (o da moderação continua). Só quem bloqueou desbloqueia (<c>CONVERSATION_UNBLOCK_FORBIDDEN</c>). Devolve se
    /// mudou (conversa já liberada é idempotente).
    /// </summary>
    public Result<bool> Unblock(ConversationParty party, DateTimeOffset now)
    {
        if (BlockedBy is null)
            return Result<bool>.Success(false);

        if (BlockedBy != party)
            return Result<bool>.Failure(ConversationsErrors.UnblockForbidden);

        BlockedBy = null;
        BlockedAt = null;
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;

        return Result<bool>.Success(true);
    }
}
