using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Reports.ResolveMessageReport;

/// <summary>
/// O admin fecha a denúncia. Inexistente: <c>404 CONVERSATION_REPORT_NOT_FOUND</c>; já analisada:
/// <c>422 CONVERSATION_REPORT_ALREADY_RESOLVED</c>; dois admins ao mesmo tempo: o segundo leva
/// <c>409 CONVERSATION_CONFLICT</c> (<c>xmin</c>). Analisada, a denúncia deixa de segurar a conversa
/// na retenção.
/// </summary>
/// <remarks>
/// <b>Procedente bloqueia a conversa pela moderação</b> (decisão do produto): ninguém envia mais, os
/// dois continuam lendo, e nenhum dos lados desbloqueia. A análise e o bloqueio gravam juntos, dentro
/// da trava da conversa (a mesma do envio), e os dois lados recebem o aviso pelo hub. Improcedente não
/// mexe na conversa. Nenhum dos dois suspende ninguém: a suspensão é feita no Owners e no Hosts.
/// </remarks>
public sealed class ResolveMessageReportCommandHandler(
    IMessageReportRepository reportRepository,
    IConversationRepository conversationRepository,
    IConversationsUnitOfWork unitOfWork,
    IConversationQueries conversationQueries,
    MessageReportResponseBuilder responseBuilder,
    ConversationNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ResolveMessageReportCommand, MessageReportResponse>
{
    public async Task<Result<MessageReportResponse>> HandleAsync(ResolveMessageReportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ReportStatusValues.TryOutcomeFromWire(command.Outcome, out var outcome))
            return Result<MessageReportResponse>.Failure(ConversationsErrors.OutcomeInvalid);

        var id = new MessageReportId(command.ReportId);
        var report = await reportRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (report is null)
            return Result<MessageReportResponse>.Failure(ConversationsErrors.ReportNotFound(id));

        var now = timeProvider.GetUtcNow();
        var resolved = report.Resolve(outcome, command.AdminUserId, command.Note, now);
        if (resolved.IsFailure)
            return Result<MessageReportResponse>.FromFailure(resolved);

        Conversation? blocked = null;

        if (outcome == ReportStatus.Upheld)
        {
            var conversation = await conversationRepository.GetByIdAsync(report.ConversationId, cancellationToken).ConfigureAwait(false);
            if (conversation is null)
                return Result<MessageReportResponse>.Failure(ConversationsErrors.Conflict);

            var saved = await SaveBlockingAsync(conversation, now, cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<MessageReportResponse>.FromFailure(saved);

            blocked = saved.Value;
        }
        else
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<MessageReportResponse>.FromFailure(saved);
        }

        await ConversationTrail.RecordReportResolvedAsync(auditTrail, report, cancellationToken).ConfigureAwait(false);

        if (blocked is not null)
        {
            await ConversationTrail
                .RecordBlockChangedAsync(auditTrail, AuditActions.ConversationBlocked, blocked, ConversationTrail.ModerationActor, cancellationToken)
                .ConfigureAwait(false);

            await notifications.BlockChangedAsync(blocked, cancellationToken).ConfigureAwait(false);
        }

        var row = await conversationQueries.GetReportAsync(id, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return Result<MessageReportResponse>.Failure(ConversationsErrors.ReportNotFound(id));

        return Result<MessageReportResponse>.Success(
            await responseBuilder.BuildAsync(row, context: null, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Grava a análise e o bloqueio da moderação numa transação só, dentro da trava da conversa.
    /// Devolve a conversa quando o bloqueio é novo (para a trilha e o aviso), ou <c>null</c> se ela já
    /// estava bloqueada pela moderação (outra denúncia procedente antes).
    /// </summary>
    private Task<Result<Conversation?>> SaveBlockingAsync(Conversation conversation, DateTimeOffset now, CancellationToken cancellationToken) =>
        unitOfWork.RunLockedOnConversationAsync(
            conversation.Id,
            async ct =>
            {
                await conversationRepository.ReloadAsync(conversation, ct).ConfigureAwait(false);
                var changed = conversation.BlockByModeration(now);

                var saved = await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

                return saved.IsFailure
                    ? Result<Conversation?>.FromFailure(saved)
                    : Result<Conversation?>.Success(changed ? conversation : null);
            },
            cancellationToken);
}
