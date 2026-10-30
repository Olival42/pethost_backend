using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;

namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>
/// Porta de leitura do módulo (§11): a Infrastructure projeta direto nas linhas com <c>Select</c>,
/// sem rastreamento. O repositório do Domain fica para o que muda o estado.
/// </summary>
public interface IConversationQueries
{
    /// <summary>
    /// As conversas de <paramref name="participant"/>, da atividade mais recente para a mais
    /// antiga, com a última mensagem e as não lidas dele.
    /// </summary>
    Task<(IReadOnlyList<ConversationRow> Items, long TotalCount)> ListAsync(
        ConversationParticipant participant,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>A conversa vista por <paramref name="viewer"/> (as não lidas são dele), ou <c>null</c>.</summary>
    Task<ConversationRow?> GetAsync(ConversationId id, ConversationParty viewer, CancellationToken cancellationToken);

    /// <summary>Total de mensagens que <paramref name="participant"/> recebeu e não leu, em todas as conversas.</summary>
    Task<int> CountUnreadAsync(ConversationParticipant participant, CancellationToken cancellationToken);

    /// <summary>
    /// Até <paramref name="take"/> mensagens da conversa, da mais nova para a mais antiga, todas
    /// anteriores a <paramref name="before"/> (quando vem). <c>null</c> quando
    /// <paramref name="before"/> não é uma mensagem desta conversa.
    /// </summary>
    Task<IReadOnlyList<MessageRow>?> ListMessagesAsync(
        ConversationId id,
        MessageId? before,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// As denúncias (todas, ou só as da situação <paramref name="status"/>), da mais antiga para a
    /// mais nova — a fila do admin —, com a mensagem denunciada.
    /// </summary>
    Task<(IReadOnlyList<ReportRow> Items, long TotalCount)> ListReportsAsync(
        ReportStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Até <paramref name="take"/> (conversa, lado) com mensagem do outro lado não lida, enviada até
    /// <paramref name="unreadSince"/> e depois do último e-mail desse lado — que, se houve, foi até
    /// <paramref name="notifiedBefore"/> (o intervalo mínimo entre dois e-mails da mesma conversa).
    /// </summary>
    Task<IReadOnlyList<UnreadNotificationRow>> ListUnreadToNotifyAsync(
        DateTimeOffset unreadSince,
        DateTimeOffset notifiedBefore,
        int take,
        CancellationToken cancellationToken);

    /// <summary>Quantas denúncias esperam o admin (<c>pending</c>).</summary>
    Task<int> CountPendingReportsAsync(CancellationToken cancellationToken);

    /// <summary>Uma denúncia com a mensagem denunciada, ou <c>null</c>.</summary>
    Task<ReportRow?> GetReportAsync(MessageReportId id, CancellationToken cancellationToken);
}
