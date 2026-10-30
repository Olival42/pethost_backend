using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Conversations.Infrastructure.Moderation;

/// <summary>
/// Aviso de denúncia nova por e-mail ao admin, pela caixa de saída (<see cref="IEmailOutbox"/>), em
/// segundo plano. Sem <see cref="ConversationModerationOptions.AdminAlertEmail"/>, não manda nada.
/// </summary>
/// <remarks>
/// O e-mail sai do sistema: leva o motivo, quem denunciou (o lado, não o nome) e o caminho na API —
/// nunca o texto da mensagem, a foto nem a descrição. Best-effort: a denúncia já está gravada; falha
/// ao enfileirar vai para o log e não derruba o pedido.
/// </remarks>
internal sealed partial class ModerationEmailAlerts(
    IEmailOutbox outbox,
    IOptions<ConversationModerationOptions> options,
    ILogger<ModerationEmailAlerts> logger) : IModerationAlerts
{
    public const string Subject = "PetHost — nova denúncia no chat";

    public async Task ReportSubmittedAsync(ReportAlert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var adminEmail = options.Value.AdminAlertEmail;
        if (string.IsNullOrWhiteSpace(adminEmail))
            return;

        string[] paragraphs =
        [
            $"Uma mensagem do chat foi denunciada pelo {Side(alert.ReportedBy)}, por {Reason(alert.Reason)}.",
            alert.PendingCount == 1
                ? "É a única denúncia esperando análise."
                : string.Create(CultureInfo.InvariantCulture, $"Há {alert.PendingCount} denúncias esperando análise."),
            $"Abra a denúncia no painel: GET /api/v1/conversations/reports/{alert.ReportId} (conversa {alert.ConversationId}).",
            "Procedente bloqueia a conversa; improcedente a deixa como está.",
        ];

        var text = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));

        try
        {
            await outbox
                .EnqueueAsync(new EmailMessage(adminEmail.Trim(), "Admin PetHost", Subject, text, html,
                    new EmailAction("Abrir painel administrativo", "/admin/users")), cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Aviso best-effort: a denúncia já foi gravada e está na fila do admin.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogAlertFailed(logger, alert.ReportId, exception);
        }
    }

    private static string Side(string reportedBy) =>
        reportedBy == ConversationPartyValues.Owner ? "tutor" : "anfitrião";

    private static string Reason(string reason) => reason switch
    {
        ReportReasonValues.Spam => "spam",
        ReportReasonValues.Harassment => "assédio ou ofensa",
        ReportReasonValues.Scam => "golpe",
        ReportReasonValues.Inappropriate => "conteúdo impróprio",
        _ => "outro motivo",
    };

    [LoggerMessage(EventId = 9110, Level = LogLevel.Warning, Message = "Moderation: report {ReportId} was saved but the admin e-mail could not be queued.")]
    private static partial void LogAlertFailed(ILogger logger, Guid reportId, Exception exception);
}
