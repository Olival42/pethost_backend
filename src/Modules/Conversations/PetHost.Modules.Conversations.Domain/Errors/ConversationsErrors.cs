using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
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

    // --- Validação: configuração da retenção (comando da tarefa em segundo plano) ---

    public static readonly Error InactivityDaysInvalid =
        Error.Validation("inactivityDays", "Inactivity days must be greater than zero.");

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

    /// <summary>409: a conversa mudou (ou foi apagada pela retenção) ao mesmo tempo.</summary>
    public static readonly Error Conflict =
        new("CONVERSATION_CONFLICT", "This conversation was changed at the same time. Reload it and try again.");
}
