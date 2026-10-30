namespace PetHost.Shared.Infrastructure.Email;

/// <summary>Um e-mail pronto para envio: destinatário, assunto e corpo em texto e HTML.</summary>
/// <param name="TextBody">Versão em texto puro, para clientes que não renderizam HTML.</param>
public sealed record EmailMessage(
    string ToAddress,
    string? ToName,
    string Subject,
    string TextBody,
    string HtmlBody,
    EmailAction? Action = null);

/// <summary>Ação do e-mail: URL HTTP(S) absoluta ou caminho do frontend configurado em Email:AppUrl.</summary>
public sealed record EmailAction(string Label, string Url);
