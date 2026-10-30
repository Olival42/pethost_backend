namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>
/// Avisa o admin de que chegou denúncia nova na fila. A Infrastructure manda e-mail para o endereço
/// da configuração (<c>ConversationModeration:AdminAlertEmail</c>); sem endereço, não manda nada.
/// </summary>
/// <remarks>
/// Best-effort: chamado depois que a denúncia foi gravada, e uma falha no aviso vai para o log sem
/// derrubar o pedido — a denúncia continua na fila. O aviso nunca leva o texto da mensagem nem a
/// descrição da denúncia: o e-mail sai do sistema.
/// </remarks>
public interface IModerationAlerts
{
    Task ReportSubmittedAsync(ReportAlert alert, CancellationToken cancellationToken);
}

/// <summary>O que o aviso ao admin leva.</summary>
/// <param name="Reason">O motivo no formato da API (<c>spam</c>, <c>harassment</c>...).</param>
/// <param name="ReportedBy"><c>owner</c> ou <c>host</c>: quem denunciou.</param>
/// <param name="PendingCount">Quantas denúncias esperam o admin, contando esta.</param>
public sealed record ReportAlert(
    Guid ReportId,
    Guid ConversationId,
    string Reason,
    string ReportedBy,
    int PendingCount);
