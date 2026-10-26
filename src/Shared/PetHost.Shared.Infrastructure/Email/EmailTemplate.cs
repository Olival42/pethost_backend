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
        var headingKey = title.StartsWith("pagamento precisa de atenção (", StringComparison.Ordinal)
            ? "pagamento precisa de atenção" : title;
        var heading = WebUtility.HtmlEncode(title);
        if (EmailHeadingAssets.Images.TryGetValue(headingKey, out var image)
            && ResolveUrl(image.Path + "?v=quintal-light-3", appUrl) is { } headingUrl)
            heading = $"""<img src="{WebUtility.HtmlEncode(headingUrl)}" width="{image.Width}" height="{image.Height}" alt="{WebUtility.HtmlEncode(title)}" style="display:block;width:100%;max-width:{image.Width}px;height:auto;border:0;color:#1F1A17;-webkit-text-fill-color:#1F1A17;font-family:'Caprasimo','Arial Black',Arial,sans-serif;font-size:30px;line-height:1.25">""";
        var logoUrl = ResolveUrl("/brand/email-logo.png?v=quintal-light-3", appUrl);
        var logo = logoUrl is null
            ? "<span class=\"email-ink\" style=\"font-family:'Caprasimo',Georgia,serif;font-size:32px;font-weight:400;color:#1F1A17\">pethost</span>"
            : $"""<img src="{WebUtility.HtmlEncode(logoUrl)}" width="177" height="52" alt="PetHost" style="display:block;width:177px;height:52px;max-width:100%;border:0;color:#1F1A17;-webkit-text-fill-color:#1F1A17;font-family:'Caprasimo',Georgia,serif;font-size:30px">""";
        var fontStyles = logoUrl is null ? string.Empty : $$"""
            <!--[if !mso]><!--><style>
            @font-face{font-family:'Caprasimo';font-style:normal;font-weight:400;font-display:swap;src:url('{{WebUtility.HtmlEncode(ResolveUrl("/fonts/caprasimo-latin-400-normal.woff2", appUrl))}}') format('woff2')}
            @font-face{font-family:'Atkinson Hyperlegible Next';font-style:normal;font-weight:400;font-display:swap;src:url('{{WebUtility.HtmlEncode(ResolveUrl("/fonts/atkinson-hyperlegible-next-latin-400-normal.woff2", appUrl))}}') format('woff2')}
            @font-face{font-family:'Atkinson Hyperlegible Next';font-style:normal;font-weight:700;font-display:swap;src:url('{{WebUtility.HtmlEncode(ResolveUrl("/fonts/atkinson-hyperlegible-next-latin-700-normal.woff2", appUrl))}}') format('woff2')}
            </style><!--<![endif]-->
            """;
        var callToAction = url is null ? string.Empty : $"""
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" bgcolor="#1F1A17" style="margin:28px 0 20px;border-radius:20px"><tr><td style="padding:0 3px 3px 0"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td bgcolor="#F26B3A" style="background-color:#F26B3A;background-image:linear-gradient(#F26B3A,#F26B3A);border:2px solid #1F1A17;border-radius:18px;text-align:center">
            <a href="{link}" style="display:inline-block;padding:16px 24px;font-family:'Atkinson Hyperlegible Next',Arial,Helvetica,sans-serif;font-size:17px;font-weight:700;color:#1F1A17;-webkit-text-fill-color:#1F1A17;text-decoration:none;border-radius:16px">{label}</a>
            </td></tr></table></td></tr></table>
            <p class="email-muted" style="font-size:13px;line-height:1.6;color:#5A4E45;-webkit-text-fill-color:#5A4E45;margin:0 0 8px">O botão leva você ao PetHost para continuar. Confira o endereço antes de informar seus dados.</p>
            <p class="email-muted" style="font-size:13px;line-height:1.6;color:#5A4E45;-webkit-text-fill-color:#5A4E45;margin:0">Se o botão não abrir, copie e cole este link no navegador:</p>
            <p style="font-size:13px;line-height:1.6;margin:6px 0 0;word-break:break-all;overflow-wrap:anywhere"><a class="email-link" href="{link}" style="color:#1F1A17;-webkit-text-fill-color:#1F1A17;text-decoration:underline">{link}</a></p>
            """;
        var textAction = url is null ? string.Empty
            : $"{Environment.NewLine}{Environment.NewLine}{action.Label}: {url}{Environment.NewLine}O link leva você ao PetHost para continuar. Confira o endereço antes de informar seus dados.";
        var html = $$"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="color-scheme" content="light only"><meta name="supported-color-schemes" content="light"><title>{{WebUtility.HtmlEncode(message.Subject)}}</title>{{fontStyles}}
            <style>
            :root{color-scheme:only light;supported-color-schemes:light}
            .email-bg,.email-card{background-color:#FEFAF3!important;background-image:linear-gradient(#FEFAF3,#FEFAF3)!important}
            .email-content,.email-content p,.email-content li,.email-content strong,.email-content b,.email-ink,.email-link,.email-content a{color:#1F1A17!important;-webkit-text-fill-color:#1F1A17!important}
            .email-muted{color:#5A4E45!important;-webkit-text-fill-color:#5A4E45!important}
            </style></head>
            <body class="email-bg" style="margin:0;padding:0;background-color:#FEFAF3;background-image:linear-gradient(#FEFAF3,#FEFAF3);color:#1F1A17;-webkit-text-fill-color:#1F1A17;font-family:'Atkinson Hyperlegible Next',Arial,Helvetica,sans-serif;-webkit-text-size-adjust:100%">
            <div style="display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all">{{WebUtility.HtmlEncode(title)}} · Um aviso do seu cantinho no PetHost.</div>
            <table class="email-bg" role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FEFAF3" style="background-color:#FEFAF3;background-image:linear-gradient(#FEFAF3,#FEFAF3);font-family:'Atkinson Hyperlegible Next',Arial,Helvetica,sans-serif"><tr><td align="center" style="padding:32px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px">
            <tr><td style="padding:0 8px 24px;font-family:'Atkinson Hyperlegible Next',Arial,Helvetica,sans-serif">{{logo}}</td></tr>
            <tr><td><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#1F1A17" style="border-radius:26px"><tr><td style="padding:0 5px 5px 0"><table class="email-card" role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FEFAF3" style="background-color:#FEFAF3;background-image:linear-gradient(#FEFAF3,#FEFAF3);border:2px solid #1F1A17;border-radius:24px"><tr><td style="padding:28px 24px;font-family:'Atkinson Hyperlegible Next',Arial,Helvetica,sans-serif">
            <p class="email-muted" style="margin:0 0 14px;font-size:11px;line-height:1.5;letter-spacing:2px;font-weight:bold;color:#5A4E45">DO NOSSO QUINTAL PARA VOCÊ</p>
            <h1 class="email-ink" style="margin:0 0 24px;font-family:'Caprasimo','Arial Black',Arial,sans-serif;font-size:30px;line-height:1.25;font-weight:400;color:#1F1A17">{{heading}}</h1>
            <div class="email-content" style="font-family:'Atkinson Hyperlegible Next',Arial,Helvetica,sans-serif;font-size:17px;line-height:1.6;color:#1F1A17;-webkit-text-fill-color:#1F1A17;overflow-wrap:break-word">{{message.HtmlBody.Replace("<p>", "<p style=\"margin:0 0 16px;font-family:'Atkinson Hyperlegible Next',Arial,Helvetica,sans-serif;font-size:17px;line-height:1.6\">", StringComparison.Ordinal)}}</div>
            {{callToAction}}
            </td></tr></table></td></tr></table></td></tr>
            <tr><td style="padding:28px 12px 12px"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr><td height="8" bgcolor="#C5DB4F" style="height:8px;border-radius:8px"></td></tr></table>
            <p class="email-ink" style="margin:20px 0 8px;font-family:'Atkinson Hyperlegible Next',Arial,Helvetica,sans-serif;font-weight:700;font-size:17px;color:#1F1A17">Até já, equipe PetHost.</p>
            <p class="email-muted" style="margin:0;font-size:12px;line-height:1.7;color:#5A4E45">Este é um aviso sobre sua conta ou uma atividade no PetHost. Nunca pedimos sua senha ou código de acesso por e-mail.</p></td></tr>
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
