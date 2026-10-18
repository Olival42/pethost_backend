namespace PetHost.Modules.Reviews.Infrastructure.Notifications;

/// <summary>
/// E-mails do módulo de avaliações (seção <c>ReviewEmail</c>). Vem do ambiente como
/// <c>ReviewEmail__AdminAlertEmail</c> etc.
/// </summary>
public sealed class ReviewEmailOptions
{
    public const string SectionName = "ReviewEmail";

    /// <summary>Endereço do app, para o botão do e-mail. Vazio = o e-mail só pede para abrir o app.</summary>
    public string? AppUrl { get; init; }

    /// <summary>
    /// E-mail que recebe o aviso de denúncia nova. Vazio = sem e-mail: o admin consulta a fila
    /// (<c>GET /api/v1/reviews/reports</c>).
    /// </summary>
    public string? AdminAlertEmail { get; init; }
}
