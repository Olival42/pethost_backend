using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Reviews.Infrastructure.Notifications;

/// <summary>
/// Os e-mails das avaliações, pela caixa de saída (<see cref="IEmailOutbox"/>), em segundo plano:
/// "você recebeu uma avaliação", o lembrete de avaliar e o aviso de denúncia à moderação.
/// </summary>
/// <remarks>
/// Nunca o texto da avaliação, da resposta nem da denúncia: só a nota, quem escreveu e o motivo. O
/// texto fica no app. Best-effort: o que disparou o e-mail já está gravado; falha ao enfileirar vai
/// para o log e não derruba o pedido.
/// </remarks>
internal sealed partial class ReviewEmailNotifier(
    IEmailOutbox outbox,
    IOptions<ReviewEmailOptions> options,
    ILogger<ReviewEmailNotifier> logger) : IReviewNotifier
{
    public const string ReceivedSubject = "PetHost — você recebeu uma avaliação";
    public const string ReminderSubject = "PetHost — como foi a estadia? Deixe sua avaliação";
    public const string ModerationSubject = "PetHost — nova denúncia de avaliação";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    /// <summary>Horário de Brasília, como o Bookings (sem horário de verão).</summary>
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public Task NotifyReceivedAsync(
        ReviewContact recipient,
        ReviewParty authorSide,
        string authorName,
        int rating,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipient);

        var who = string.IsNullOrWhiteSpace(authorName)
            ? (authorSide == ReviewParty.Owner ? "Um tutor" : "Um anfitrião")
            : authorName;

        string[] paragraphs =
        [
            $"Olá, {recipient.Name}.",
            string.Create(PtBr, $"{who} avaliou a estadia com nota {rating} de 5."),
            authorSide == ReviewParty.Owner
                ? "A avaliação já aparece no seu perfil. Se quiser, responda por lá: a resposta também é pública."
                : "A avaliação fica visível para os anfitriões a quem você pedir estadia. Se quiser, responda por lá.",
            CallToAction("ler a avaliação"),
        ];

        return EnqueueAsync(recipient.Email, recipient.Name, ReceivedSubject, paragraphs, cancellationToken);
    }

    public Task SendReminderAsync(
        ReviewContact recipient,
        ReviewParty recipientSide,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipient);

        var until = deadline.ToOffset(Brasilia).ToString("dd/MM 'às' HH'h'mm", PtBr);

        string[] paragraphs =
        [
            $"Olá, {recipient.Name}.",
            recipientSide == ReviewParty.Owner
                ? "O seu pet já voltou para casa. Conte como foi a estadia: a sua avaliação ajuda outros tutores a escolher."
                : "A estadia terminou. Conte como foi com o tutor e com os pets: a sua avaliação ajuda outros anfitriões.",
            $"Você pode avaliar até {until}.",
            CallToAction("avaliar"),
        ];

        return EnqueueAsync(recipient.Email, recipient.Name, ReminderSubject, paragraphs, cancellationToken);
    }

    public Task AlertModeratorsAsync(ReviewReportId reportId, ReviewReportReason reason, CancellationToken cancellationToken)
    {
        var adminEmail = options.Value.AdminAlertEmail;
        if (string.IsNullOrWhiteSpace(adminEmail))
            return Task.CompletedTask;

        string[] paragraphs =
        [
            $"Uma avaliação foi denunciada, por {Reason(reason)}.",
            $"Abra a denúncia no painel: GET /api/v1/reviews/reports/{reportId}.",
            "Procedente oculta a avaliação; improcedente a deixa como está.",
        ];

        return EnqueueAsync(adminEmail.Trim(), "Admin PetHost", ModerationSubject, paragraphs, cancellationToken);
    }

    private string CallToAction(string action)
    {
        var appUrl = options.Value.AppUrl?.Trim();

        return string.IsNullOrEmpty(appUrl)
            ? $"Abra o app do PetHost para {action}."
            : $"Abra o PetHost para {action}: {appUrl}";
    }

    private async Task EnqueueAsync(
        string email,
        string name,
        string subject,
        string[] paragraphs,
        CancellationToken cancellationToken)
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));

        try
        {
            await outbox.EnqueueAsync(new EmailMessage(email, name, subject, text, html), cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Aviso best-effort: o que disparou o e-mail já foi gravado.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogEnqueueFailed(logger, subject, exception);
        }
    }

    private static string Reason(ReviewReportReason reason) => reason switch
    {
        ReviewReportReason.Offensive => "ofensa",
        ReviewReportReason.FalseInformation => "informação falsa",
        ReviewReportReason.PersonalData => "dado pessoal exposto",
        ReviewReportReason.Spam => "spam",
        _ => "outro motivo",
    };

    [LoggerMessage(EventId = 9210, Level = LogLevel.Warning, Message = "Reviews: the e-mail '{Subject}' could not be queued.")]
    private static partial void LogEnqueueFailed(ILogger logger, string subject, Exception exception);
}
