using System.Net;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>Identidade Quintal de todos os e-mails da outbox; HTML de conteúdo vem dos notifiers, já escapado.</summary>
internal static class EmailTemplate
{
    internal static EmailMessage Render(EmailMessage message, string? appUrl)
    {
        var title = message.Subject.StartsWith("PetHost — ", StringComparison.Ordinal)
            ? message.Subject[10..]
            : message.Subject;
        var action = message.Action ?? new EmailAction("Abrir o PetHost", "/");
        var url = ResolveUrl(action.Url, appUrl);
        var link = WebUtility.HtmlEncode(url);
        var label = WebUtility.HtmlEncode(action.Label);
        var callToAction = url is null ? string.Empty : $"""
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:28px 0 20px"><tr><td bgcolor="#F26B3A" style="border:2px solid #1F1A17;border-radius:16px;box-shadow:3px 3px 0 #1F1A17;text-align:center">
            <a href="{link}" style="display:inline-block;padding:16px 24px;font-family:Arial,Helvetica,sans-serif;font-size:16px;font-weight:bold;color:#1F1A17;text-decoration:none;border-radius:14px">{label}</a>
            </td></tr></table>
            <p style="font-size:13px;line-height:1.6;color:#5A4E45;margin:0 0 8px">O botão leva você ao PetHost para continuar. Confira o endereço antes de informar seus dados.</p>
            <p style="font-size:13px;line-height:1.6;color:#5A4E45;margin:0">Se o botão não abrir, copie e cole este link no navegador:</p>
            <p style="font-size:13px;line-height:1.6;margin:6px 0 0;word-break:break-all;overflow-wrap:anywhere"><a href="{link}" style="color:#1F1A17;text-decoration:underline">{link}</a></p>
            """;
        var textAction = url is null ? string.Empty
            : $"{Environment.NewLine}{Environment.NewLine}{action.Label}: {url}{Environment.NewLine}O link leva você ao PetHost para continuar. Confira o endereço antes de informar seus dados.";
        var html = $$"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="color-scheme" content="light"><title>{{WebUtility.HtmlEncode(message.Subject)}}</title></head>
            <body style="margin:0;padding:0;background-color:#FEFAF3;color:#1F1A17;font-family:Arial,Helvetica,sans-serif;-webkit-text-size-adjust:100%">
            <div style="display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all">{{WebUtility.HtmlEncode(title)}} · Um aviso do seu cantinho no PetHost.</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FEFAF3"><tr><td align="center" style="padding:32px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px">
            <tr><td style="padding:0 8px 24px"><span style="font-family:Georgia,'Times New Roman',serif;font-size:32px;line-height:1;font-weight:bold;letter-spacing:-1px;color:#1F1A17">pethost<span style="color:#F26B3A">.</span></span><p style="margin:8px 0 0;font-size:13px;line-height:1.5;color:#5A4E45">Um cantinho de confiança para quem faz parte da família.</p></td></tr>
            <tr><td bgcolor="#FFFFFF" style="padding:28px 24px;border:2px solid #1F1A17;border-radius:24px;box-shadow:4px 4px 0 #1F1A17">
            <p style="margin:0 0 14px;font-size:11px;line-height:1.5;letter-spacing:2px;font-weight:bold;color:#5A4E45">DO NOSSO QUINTAL PARA VOCÊ</p>
            <h1 style="margin:0 0 24px;font-family:Georgia,'Times New Roman',serif;font-size:30px;line-height:1.2;font-weight:bold;color:#1F1A17">{{WebUtility.HtmlEncode(title)}}</h1>
            <div style="font-size:16px;line-height:1.75;color:#1F1A17;overflow-wrap:break-word">{{message.HtmlBody.Replace("<p>", "<p style=\"margin:0 0 16px\">", StringComparison.Ordinal)}}</div>
            {{callToAction}}
            </td></tr>
            <tr><td style="padding:28px 12px 12px"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr><td height="8" bgcolor="#C5DB4F" style="height:8px;border-radius:8px"></td></tr></table>
            <p style="margin:20px 0 8px;font-family:Georgia,'Times New Roman',serif;font-weight:bold;font-size:20px;color:#1F1A17">Até já, equipe PetHost.</p>
            <p style="margin:0;font-size:12px;line-height:1.7;color:#5A4E45">Este é um aviso sobre sua conta ou uma atividade no PetHost. Nunca pedimos sua senha ou código de acesso por e-mail.</p></td></tr>
            </table></td></tr></table></body></html>
            """;
        return message with
        {
            HtmlBody = html,
            TextBody = message.TextBody + textAction + Environment.NewLine + Environment.NewLine
                + "Até já, equipe PetHost. Nunca pedimos sua senha ou código de acesso por e-mail.",
            Action = null,
        };
    }

    private static string? ResolveUrl(string value, string? appUrl)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp)
            && string.IsNullOrEmpty(absolute.UserInfo))
            return absolute.AbsoluteUri;

        if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains('\\') || value.Any(char.IsControl)
            || !Uri.TryCreate(appUrl, UriKind.Absolute, out var origin)
            || (origin.Scheme != Uri.UriSchemeHttps && origin.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(origin.UserInfo))
            return null;

        return new Uri(origin, value).AbsoluteUri;
    }
}
