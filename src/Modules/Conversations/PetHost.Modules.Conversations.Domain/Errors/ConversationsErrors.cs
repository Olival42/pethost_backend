using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Conversations.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Conversations (chat). Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class ConversationsErrors
{
    /// <summary>Maior página das listagens (§13).</summary>
    public const int MaxPageSize = 100;

    /// <summary>Maior lote de cada consulta da retenção.</summary>
    public const int MaxRetentionBatchSize = 1000;

    // --- Validação (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error ListingIdRequired =
        Error.Validation("listingId", "Listing id is required.");

    public static readonly Error ConversationIdRequired =
        Error.Validation("conversationId", "Conversation id is required.");

    public static readonly Error BodyRequired =
        Error.Validation("body", "Message text is required.");

    public static readonly Error BodyTooLong =
        Error.Validation("body", $"Message text must be at most {Message.BodyMaxLength} characters.");

    public static readonly Error BodyInvalidCharacters =
        Error.Validation("body", "Message text must not contain control characters other than line breaks and tabs.");

    // A URL da foto vem do bucket, não do cliente: estes dois só aparecem com o storage mal configurado.
    public static readonly Error ImageUrlInvalid =
        Error.Validation("imageUrl", "Image URL must be an absolute http or https address.");

    public static readonly Error ImageUrlTooLong =
        Error.Validation("imageUrl", $"Image URL must be at most {Message.ImageUrlMaxLength} characters.");

    public static readonly Error PageInvalid =
        Error.Validation("page", "Page must be 1 or greater.");

    public static readonly Error PageSizeInvalid =
        Error.Validation("pageSize", $"Page size must be between 1 and {MaxPageSize}.");

    public static readonly Error BeforeInvalid =
        Error.Validation("before", "Before must be the id of a message in this conversation.");

    // --- Validação: moderação (denúncia e análise do admin) ---

    public static readonly Error MessageIdRequired =
        Error.Validation("messageId", "Message id is required.");

    public static readonly Error ReportIdRequired =
        Error.Validation("reportId", "Report id is required.");

    public static readonly Error ReasonInvalid =
        Error.Validation("reason", $"Reason must be one of: {string.Join(", ", ReportReasonValues.All)}.");

    public static readonly Error DescriptionRequired =
        Error.Validation("description", $"Description is required when the reason is '{ReportReasonValues.Other}'.");

    public static readonly Error DescriptionTooLong =
        Error.Validation("description", $"Description must be at most {MessageReport.DescriptionMaxLength} characters.");

    public static readonly Error DescriptionInvalidCharacters =
        Error.Validation("description", "Description must not contain control characters other than line breaks and tabs.");

    public static readonly Error OutcomeInvalid =
        Error.Validation("outcome", $"Outcome must be one of: {string.Join(", ", ReportStatusValues.Outcomes)}.");

    public static readonly Error NoteRequired =
        Error.Validation("note", "A note is required to resolve a report.");

    public static readonly Error NoteTooLong =
        Error.Validation("note", $"Note must be at most {MessageReport.ResolutionNoteMaxLength} characters.");

    public static readonly Error NoteInvalidCharacters =
        Error.Validation("note", "Note must not contain control characters other than line breaks and tabs.");

    public static readonly Error UnreadMessagesEmailRequired =
        Error.Validation("unreadMessagesEmail", "Unread messages e-mail choice is required (true or false).");

    public static readonly Error StatusInvalid =
        Error.Validation("status", $"Status must be one of: {string.Join(", ", ReportStatusValues.All)}.");

    // --- Validação: configuração da retenção (comando da tarefa em segundo plano) ---

    public static readonly Error InactivityDaysInvalid =
        Error.Validation("inactivityDays", "Inactivity days must be greater than zero.");

    public static readonly Error UnreadDelayInvalid =
        Error.Validation("delayMinutes", "Unread e-mail delay must be greater than zero.");

    public static readonly Error UnreadCooldownInvalid =
        Error.Validation("cooldownMinutes", "Unread e-mail cooldown must be greater than zero.");

    public static readonly Error BatchSizeInvalid =
        Error.Validation("batchSize", $"Batch size must be between 1 and {MaxRetentionBatchSize}.");

    // --- Regras de negócio ---

    /// <summary>
    /// 404: não existe conversa com esse id, ou ela é de outra pessoa. As duas situações respondem
    /// igual de propósito (§8).
    /// </summary>
    public static Error NotFound(ConversationId id) =>
        new("CONVERSATION_NOT_FOUND", $"Conversation '{id.Value}' was not found.");

    /// <summary>404: o cantinho não existe ou não aparece na busca (inativo, anfitrião indisponível).</summary>
    public static Error ListingNotFound(Guid listingId) =>
        new("CONVERSATION_LISTING_NOT_FOUND", $"Listing '{listingId}' was not found.");

    /// <summary>404: a conta do token não tem perfil de tutor.</summary>
    public static readonly Error OwnerNotFound =
        new("CONVERSATION_OWNER_NOT_FOUND", "This account has no owner profile.");

    /// <summary>404: a conta do token não tem perfil de anfitrião verificado.</summary>
    public static readonly Error HostNotFound =
        new("CONVERSATION_HOST_NOT_FOUND", "This account has no verified host profile yet.");

    /// <summary>403: o tutor está inativo ou suspenso e não pode puxar conversa nem enviar mensagem.</summary>
    public static readonly Error OwnerDeactivated =
        new("CONVERSATION_OWNER_DEACTIVATED", "This owner profile is deactivated or suspended.");

    /// <summary>403: o anfitrião está inativo ou suspenso e não pode enviar mensagem.</summary>
    public static readonly Error HostDeactivated =
        new("CONVERSATION_HOST_DEACTIVATED", "This host profile is deactivated or suspended.");

    /// <summary>
    /// 403: o outro lado da conversa foi suspenso pelo admin e não recebe mensagens. Vale nos dois
    /// sentidos (decisão do produto). Inativo continua recebendo: reativa sozinho e lê depois.
    /// </summary>
    public static readonly Error RecipientSuspended =
        new("CONVERSATION_RECIPIENT_SUSPENDED", "The other participant is suspended and cannot receive messages.");

    /// <summary>
    /// 409: a dupla já tem conversa. Só aparece na corrida de duas aberturas ao mesmo tempo; o
    /// caso de uso devolve a conversa existente.
    /// </summary>
    public static readonly Error AlreadyExists =
        new("CONVERSATION_ALREADY_EXISTS", "There is already a conversation between this owner and this host.");

    /// <summary>
    /// 422: a conversa está bloqueada (por qualquer um dos lados) e ninguém envia. Os dois continuam
    /// lendo o histórico; só quem bloqueou desbloqueia.
    /// </summary>
    public static readonly Error Blocked =
        new("CONVERSATION_BLOCKED", "This conversation is blocked. No messages can be sent until it is unblocked.");

    /// <summary>
    /// 422: a moderação bloqueou a conversa (o admin julgou procedente uma denúncia) e ninguém envia.
    /// Os dois continuam lendo; nenhum dos lados desbloqueia.
    /// </summary>
    public static readonly Error BlockedByModeration =
        new("CONVERSATION_BLOCKED_BY_MODERATION", "This conversation was blocked by moderation. No messages can be sent.");

    /// <summary>422: o outro lado já bloqueou a conversa.</summary>
    public static readonly Error AlreadyBlocked =
        new("CONVERSATION_ALREADY_BLOCKED", "This conversation is already blocked by the other participant.");

    /// <summary>403: só quem bloqueou a conversa desbloqueia.</summary>
    public static readonly Error UnblockForbidden =
        new("CONVERSATION_UNBLOCK_FORBIDDEN", "Only the participant who blocked this conversation can unblock it.");

    /// <summary>404: não existe mensagem com esse id nesta conversa.</summary>
    public static Error MessageNotFound(MessageId id) =>
        new("CONVERSATION_MESSAGE_NOT_FOUND", $"Message '{id.Value}' was not found in this conversation.");

    /// <summary>422: só quem recebeu a mensagem denuncia; a própria mensagem, não.</summary>
    public static readonly Error ReportOwnMessage =
        new("CONVERSATION_REPORT_OWN_MESSAGE", "You cannot report a message you sent.");

    /// <summary>409: esta mensagem já foi denunciada (uma denúncia por mensagem).</summary>
    public static readonly Error ReportAlreadyExists =
        new("CONVERSATION_REPORT_ALREADY_EXISTS", "This message has already been reported.");

    /// <summary>404: não existe denúncia com esse id.</summary>
    public static Error ReportNotFound(MessageReportId id) =>
        new("CONVERSATION_REPORT_NOT_FOUND", $"Report '{id.Value}' was not found.");

    /// <summary>422: a denúncia já foi analisada.</summary>
    public static readonly Error ReportAlreadyResolved =
        new("CONVERSATION_REPORT_ALREADY_RESOLVED", "This report has already been resolved.");

    /// <summary>409: a conversa mudou (ou foi apagada pela retenção) ao mesmo tempo.</summary>
    public static readonly Error Conflict =
        new("CONVERSATION_CONFLICT", "This conversation was changed at the same time. Reload it and try again.");
}
