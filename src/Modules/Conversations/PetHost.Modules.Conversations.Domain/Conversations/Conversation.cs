using PetHost.Shared.Kernel.Domain;

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
}
