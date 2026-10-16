namespace PetHost.Modules.Conversations.Infrastructure.Moderation;

/// <summary>Configuração da moderação do chat (seção <c>ConversationModeration</c>).</summary>
public sealed class ConversationModerationOptions
{
    public const string SectionName = "ConversationModeration";

    /// <summary>
    /// E-mail que recebe o aviso de denúncia nova. Vazio = sem e-mail: o admin consulta a fila
    /// (<c>GET /api/v1/conversations/reports</c>).
    /// </summary>
    public string? AdminAlertEmail { get; init; }
}
