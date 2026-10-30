using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Reports.GetMessageReportById;

/// <summary>
/// A denúncia com a mensagem denunciada e até <see cref="ContextSize"/> mensagens anteriores a ela,
/// para o admin entender a conversa sem ler tudo. Inexistente: <c>404 CONVERSATION_REPORT_NOT_FOUND</c>.
/// Abrir vai para a trilha (acesso a conversa privada).
/// </summary>
public sealed class GetMessageReportByIdQueryHandler(
    IConversationQueries conversationQueries,
    MessageReportResponseBuilder responseBuilder,
    IAuditTrail auditTrail) : IQueryHandler<GetMessageReportByIdQuery, MessageReportResponse>
{
    /// <summary>Quantas mensagens anteriores à denunciada vêm como contexto.</summary>
    public const int ContextSize = 10;

    public async Task<Result<MessageReportResponse>> HandleAsync(GetMessageReportByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var id = new MessageReportId(query.ReportId);
        var row = await conversationQueries.GetReportAsync(id, cancellationToken).ConfigureAwait(false);

        if (row is null)
            return Result<MessageReportResponse>.Failure(ConversationsErrors.ReportNotFound(id));

        var context = await conversationQueries
            .ListMessagesAsync(row.ConversationId, row.Message.Id, ContextSize, cancellationToken)
            .ConfigureAwait(false);

        await ConversationTrail.RecordReportViewedAsync(auditTrail, id, cancellationToken).ConfigureAwait(false);

        return Result<MessageReportResponse>.Success(
            await responseBuilder.BuildAsync(row, context ?? [], cancellationToken).ConfigureAwait(false));
    }
}
