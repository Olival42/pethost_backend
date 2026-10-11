using System.Net;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Hosts.Infrastructure.Notifications;

/// <summary>
/// Monta o aviso da decisão (aprovado ou recusado, com o motivo em linguagem simples) e deixa
/// na caixa de saída. O envio acontece em segundo plano (<see cref="IEmailOutbox"/>).
/// </summary>
/// <remarks>Sem CPF, sem nota interna do admin: só o que a pessoa precisa para agir.</remarks>
internal sealed class HostApplicationEmailNotifier(IEmailOutbox outbox) : IHostApplicationNotifier
{
    private const string ApprovedSubject = "PetHost — seu cadastro de anfitrião foi aprovado!";
    private const string RejectedSubject = "PetHost — precisamos de uma nova verificação";

    /// <summary>O motivo da recusa, do jeito que a pessoa entende.</summary>
    private static readonly Dictionary<string, string> ReasonText = new(StringComparer.Ordinal)
    {
        ["document_illegible"] = "Não conseguimos ler o documento nas fotos. Tente com boa luz, sem reflexo e com o documento inteiro aparecendo.",
        ["document_expired"] = "O documento enviado está vencido. Envie um documento dentro da validade.",
        ["selfie_mismatch"] = "A selfie precisa mostrar o seu rosto e o documento na mesma foto.",
        ["data_mismatch"] = "Os dados do documento não batem com os do seu cadastro (nome, nascimento ou CPF).",
        ["missing_document"] = "Uma das fotos não era a esperada. Confira frente, verso e selfie.",
        ["suspected_fraud"] = "Não foi possível confirmar a sua identidade.",
        ["other"] = "Não foi possível confirmar a sua identidade com as fotos enviadas.",
    };

    public async Task NotifyApprovedAsync(HostApplicationDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var text = string.Join(
            Environment.NewLine,
            $"Olá, {decision.FullName}.",
            string.Empty,
            "Conferimos os seus documentos e o seu cadastro de anfitrião foi aprovado.",
            "O próximo passo é cadastrar a conta de recebimento e montar o seu cantinho no app.");

        var html = $"""
            <p>Olá, {WebUtility.HtmlEncode(decision.FullName)}.</p>
            <p>Conferimos os seus documentos e o seu cadastro de anfitrião foi <strong>aprovado</strong>.</p>
            <p>O próximo passo é cadastrar a conta de recebimento e montar o seu cantinho no app.</p>
            """;

        await outbox
            .EnqueueAsync(new EmailMessage(decision.Email, decision.FullName, ApprovedSubject, text, html), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task NotifyRejectedAsync(HostApplicationDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var reason = ReasonText.GetValueOrDefault(decision.RejectionReason ?? string.Empty, ReasonText["other"]);
        var next = decision.CanTryAgain
            ? "Você pode enviar uma nova verificação pelo app quando quiser."
            : "Para continuar, fale com o suporte do PetHost.";

        var text = string.Join(
            Environment.NewLine,
            $"Olá, {decision.FullName}.",
            string.Empty,
            "Analisamos o seu cadastro de anfitrião e ainda não foi possível aprovar.",
            reason,
            next);

        var html = $"""
            <p>Olá, {WebUtility.HtmlEncode(decision.FullName)}.</p>
            <p>Analisamos o seu cadastro de anfitrião e ainda não foi possível aprovar.</p>
            <p>{WebUtility.HtmlEncode(reason)}</p>
            <p>{WebUtility.HtmlEncode(next)}</p>
            """;

        await outbox
            .EnqueueAsync(new EmailMessage(decision.Email, decision.FullName, RejectedSubject, text, html), cancellationToken)
            .ConfigureAwait(false);
    }
}
