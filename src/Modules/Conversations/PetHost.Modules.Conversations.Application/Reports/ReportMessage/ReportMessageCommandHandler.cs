using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Reports.ReportMessage;

/// <summary>
/// Registra a denúncia de uma mensagem para a fila do admin. Só o tutor e o anfitrião da conversa
/// (os outros: <c>404 CONVERSATION_NOT_FOUND</c>), e só quem <b>recebeu</b> a mensagem
/// (<c>422 CONVERSATION_REPORT_OWN_MESSAGE</c>). Uma denúncia por mensagem
/// (<c>409 CONVERSATION_REPORT_ALREADY_EXISTS</c>, barrada pelo índice único). Não exige perfil
/// ativo e não bloqueia a conversa: bloquear é outra rota. Gravada, o admin é avisado
/// (<see cref="IModerationAlerts"/>, e-mail se configurado) com o motivo e quantas esperam na fila.
/// </summary>
public sealed class ReportMessageCommandHandler(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IMessageReportRepository reportRepository,
    IConversationsUnitOfWork unitOfWork,
    IConversationParties conversationParties,
    IConversationQueries conversationQueries,
    IModerationAlerts moderationAlerts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReportMessageCommand, SubmittedReportResponse>
{
    public async Task<Result<SubmittedReportResponse>> HandleAsync(ReportMessageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await ConversationAccess
            .LoadAsync(conversationRepository, conversationParties, command.UserId, command.Role, command.ConversationId, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<SubmittedReportResponse>.FromFailure(loaded);

        var (conversation, participant) = loaded.Value!;

        var messageId = new MessageId(command.MessageId);
        var message = await messageRepository
            .FindInConversationAsync(conversation.Id, messageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
            return Result<SubmittedReportResponse>.Failure(ConversationsErrors.MessageNotFound(messageId));

        if (!ReportReasonValues.TryFromWire(command.Reason, out var reason))
            return Result<SubmittedReportResponse>.Failure(ConversationsErrors.ReasonInvalid);

        var report = MessageReport.Submit(message, participant.Side, reason, command.Description, timeProvider.GetUtcNow());
        if (report.IsFailure)
            return Result<SubmittedReportResponse>.FromFailure(report);

        reportRepository.Add(report.Value!);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<SubmittedReportResponse>.FromFailure(saved);

        await ConversationTrail.RecordMessageReportedAsync(auditTrail, report.Value!, cancellationToken).ConfigureAwait(false);

        var created = report.Value!;

        var pending = await conversationQueries.CountPendingReportsAsync(cancellationToken).ConfigureAwait(false);
        await moderationAlerts
            .ReportSubmittedAsync(
                new ReportAlert(
                    created.Id.Value,
                    created.ConversationId.Value,
                    ReportReasonValues.ToWire(created.Reason),
                    ConversationPartyValues.ToWire(created.ReportedBy),
                    pending),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<SubmittedReportResponse>.Success(new SubmittedReportResponse(
            created.Id.Value,
            created.ConversationId.Value,
            created.MessageId.Value,
            ReportReasonValues.ToWire(created.Reason),
            ReportStatusValues.ToWire(created.Status),
            created.CreatedAt));
    }
}
