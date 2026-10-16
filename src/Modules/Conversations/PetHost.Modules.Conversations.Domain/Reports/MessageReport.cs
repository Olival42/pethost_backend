using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Domain.Reports;

/// <summary>
/// Denúncia de uma mensagem do chat, feita por quem a recebeu, para o admin analisar. Espelha a
/// tabela <c>conversation.message_reports</c>.
/// </summary>
/// <remarks>
/// Quem denunciou é guardado pelo lado (<see cref="ConversationParty"/>), como na mensagem: a
/// conversa tem exatamente dois lados, e só o que recebeu a mensagem denuncia. Uma denúncia por
/// mensagem. Enquanto <see cref="ReportStatus.Pending"/>, a retenção não apaga a conversa: o admin
/// precisa ver a mensagem e a foto. A análise do admin fecha a denúncia; suspender a conta continua
/// nos endpoints do Owners e do Hosts.
/// </remarks>
public sealed class MessageReport : Entity<MessageReportId>
{
    /// <summary>Tamanho máximo da descrição de quem denunciou.</summary>
    public const int DescriptionMaxLength = 1000;

    /// <summary>Tamanho máximo da observação do admin ao analisar.</summary>
    public const int ResolutionNoteMaxLength = 1000;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private MessageReport()
    {
    }

    private MessageReport(
        MessageId messageId,
        ConversationId conversationId,
        ConversationParty reportedBy,
        ReportReason reason,
        string? description,
        DateTimeOffset now)
        : base(MessageReportId.New())
    {
        MessageId = messageId;
        ConversationId = conversationId;
        ReportedBy = reportedBy;
        Reason = reason;
        Description = description;
        Status = ReportStatus.Pending;
        CreatedAt = now;
    }

    /// <summary>A mensagem denunciada.</summary>
    public MessageId MessageId { get; private set; }

    /// <summary>A conversa da mensagem (a retenção confere as denúncias pendentes por ela).</summary>
    public ConversationId ConversationId { get; private set; }

    /// <summary>Quem denunciou: o lado que recebeu a mensagem.</summary>
    public ConversationParty ReportedBy { get; private set; }

    public ReportReason Reason { get; private set; }

    /// <summary>O que quem denunciou contou, sem espaços nas pontas. Obrigatória no motivo <c>other</c>.</summary>
    public string? Description { get; private set; }

    public ReportStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Quando o admin analisou. Nulo enquanto pendente.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>Conta do admin que analisou (<c>auth.users.id</c>). Nula enquanto pendente.</summary>
    public Guid? ResolvedBy { get; private set; }

    /// <summary>O que o admin anotou ao analisar. Nula enquanto pendente.</summary>
    public string? ResolutionNote { get; private set; }

    /// <summary>A denúncia ainda espera o admin.</summary>
    public bool IsPending => Status == ReportStatus.Pending;

    /// <summary>
    /// <paramref name="reporter"/> denuncia a <paramref name="message"/>. Só quem recebeu denuncia:
    /// a própria mensagem é <c>CONVERSATION_REPORT_OWN_MESSAGE</c>. Os erros da descrição voltam juntos.
    /// </summary>
    public static Result<MessageReport> Submit(
        Message message,
        ConversationParty reporter,
        ReportReason reason,
        string? description,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.SentBy == reporter)
            return Result<MessageReport>.Failure(ConversationsErrors.ReportOwnMessage);

        var text = Normalize(description);
        var errors = new List<Error>();

        if (text is null && reason == ReportReason.Other)
            errors.Add(ConversationsErrors.DescriptionRequired);

        if (text is { Length: > DescriptionMaxLength })
            errors.Add(ConversationsErrors.DescriptionTooLong);

        if (text is not null && Message.HasControlCharacters(text))
            errors.Add(ConversationsErrors.DescriptionInvalidCharacters);

        if (errors.Count > 0)
            return Result<MessageReport>.Failure(errors);

        return Result<MessageReport>.Success(
            new MessageReport(message.Id, message.ConversationId, reporter, reason, text, now));
    }

    /// <summary>
    /// O admin <paramref name="adminUserId"/> analisa a denúncia: <paramref name="outcome"/> é
    /// <see cref="ReportStatus.Upheld"/> ou <see cref="ReportStatus.Dismissed"/>, com observação
    /// obrigatória (vai para a trilha como motivo). Já analisada: <c>CONVERSATION_REPORT_ALREADY_RESOLVED</c>.
    /// </summary>
    public Result Resolve(ReportStatus outcome, Guid adminUserId, string? note, DateTimeOffset now)
    {
        if (outcome == ReportStatus.Pending)
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A resolution must be upheld or dismissed.");

        if (adminUserId == Guid.Empty)
            throw new ArgumentException("The admin id cannot be empty.", nameof(adminUserId));

        if (!IsPending)
            return Result.Failure(ConversationsErrors.ReportAlreadyResolved);

        var text = Normalize(note);
        var errors = new List<Error>();

        if (text is null)
            errors.Add(ConversationsErrors.NoteRequired);

        if (text is { Length: > ResolutionNoteMaxLength })
            errors.Add(ConversationsErrors.NoteTooLong);

        if (text is not null && Message.HasControlCharacters(text))
            errors.Add(ConversationsErrors.NoteInvalidCharacters);

        if (errors.Count > 0)
            return Result.Failure(errors);

        Status = outcome;
        ResolvedAt = now;
        ResolvedBy = adminUserId;
        ResolutionNote = text;

        return Result.Success();
    }

    /// <summary>Texto sem espaços nas pontas; vazio vira nulo.</summary>
    private static string? Normalize(string? text)
    {
        var trimmed = text?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
