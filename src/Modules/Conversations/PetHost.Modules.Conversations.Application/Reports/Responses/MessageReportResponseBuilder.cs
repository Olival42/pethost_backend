using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Reports;

namespace PetHost.Modules.Conversations.Application.Reports.Responses;

/// <summary>
/// Junta às denúncias lidas do banco o nome e a foto de quem denunciou e de quem foi denunciado,
/// numa consulta por módulo para a página inteira (nunca uma por denúncia).
/// </summary>
public sealed class MessageReportResponseBuilder(IConversationParties conversationParties, MessagePhotoLinks photoLinks)
{
    /// <param name="context">As mensagens anteriores (só no detalhe).</param>
    public async Task<MessageReportResponse> BuildAsync(
        ReportRow row,
        IReadOnlyList<MessageRow>? context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        var responses = await BuildAsync([row], cancellationToken).ConfigureAwait(false);

        return context is null
            ? responses[0]
            : responses[0] with { Context = await photoLinks.ToResponsesAsync(context, cancellationToken).ConfigureAwait(false) };
    }

    public async Task<IReadOnlyList<MessageReportResponse>> BuildAsync(
        IReadOnlyList<ReportRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
            return [];

        var owners = await conversationParties
            .GetOwnersAsync([.. rows.Select(r => r.OwnerId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var hosts = await conversationParties
            .GetHostsAsync([.. rows.Select(r => r.HostId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var messages = new Dictionary<Guid, MessageResponse>();
        foreach (var row in rows)
            messages[row.Id.Value] = await photoLinks.ToResponseAsync(row.Message, cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(row =>
        {
            var owner = Summary(owners.GetValueOrDefault(row.OwnerId));
            var host = Summary(hosts.GetValueOrDefault(row.HostId));
            var reporterIsOwner = row.ReportedBy == ConversationParty.Owner;

            return new MessageReportResponse(
                row.Id.Value,
                row.ConversationId.Value,
                ReportStatusValues.ToWire(row.Status),
                ReportReasonValues.ToWire(row.Reason),
                row.Description,
                ConversationPartyValues.ToWire(row.ReportedBy),
                reporterIsOwner ? owner : host,
                reporterIsOwner ? host : owner,
                messages[row.Id.Value],
                Context: null,
                row.CreatedAt,
                row.ResolvedAt,
                row.ResolvedBy,
                row.ResolutionNote);
        })];
    }

    private static PartySummary? Summary(PartyInfo? party) =>
        party is null ? null : new PartySummary(party.Id, party.Name, party.AvatarUrl);
}
