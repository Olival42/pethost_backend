using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Reports.ListMessageReports;

/// <summary>
/// A fila do admin, da denúncia mais antiga para a mais nova, com a mensagem denunciada e quem é
/// quem. Paginada (§13); situação ou paginação inválida: <c>400</c> com os campos. A listagem vai
/// para a trilha: o admin viu mensagens privadas.
/// </summary>
public sealed class ListMessageReportsQueryHandler(
    IConversationQueries conversationQueries,
    MessageReportResponseBuilder responseBuilder,
    IAuditTrail auditTrail) : IQueryHandler<ListMessageReportsQuery, PagedResult<MessageReportResponse>>
{
    public async Task<Result<PagedResult<MessageReportResponse>>> HandleAsync(
        ListMessageReportsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new List<Error>();
        ReportStatus? status = null;

        if (query.Status is not null)
        {
            if (ReportStatusValues.TryFromWire(query.Status, out var parsed))
                status = parsed;
            else
                errors.Add(ConversationsErrors.StatusInvalid);
        }

        if (query.Page < 1)
            errors.Add(ConversationsErrors.PageInvalid);

        if (query.PageSize is < 1 or > ConversationsErrors.MaxPageSize)
            errors.Add(ConversationsErrors.PageSizeInvalid);

        if (errors.Count > 0)
            return Result<PagedResult<MessageReportResponse>>.Failure(errors);

        var (rows, total) = await conversationQueries
            .ListReportsAsync(status, query.Page, query.PageSize, cancellationToken)
            .ConfigureAwait(false);

        var items = await responseBuilder.BuildAsync(rows, cancellationToken).ConfigureAwait(false);

        await ConversationTrail
            .RecordReportsListedAsync(auditTrail, query.Status, query.Page, items.Count, cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<MessageReportResponse>>.Success(
            new PagedResult<MessageReportResponse>(items, query.Page, query.PageSize, total));
    }
}
