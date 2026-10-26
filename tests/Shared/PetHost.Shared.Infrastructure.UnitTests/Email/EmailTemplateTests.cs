using FluentAssertions;
using PetHost.Shared.Infrastructure.Email;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Email;

public sealed class EmailTemplateTests
{
    public static IEnumerable<object[]> ProductionHeadings => EmailHeadingAssets.Images.Keys.Select(title => new object[] { title });

    [Theory]
    [MemberData(nameof(ProductionHeadings))]
    public void Render_Should_PreserveTheExactDisplayFontForEveryProductionHeading(string title)
    {
        var message = new EmailMessage("test@example.invalid", "Ana", $"PetHost — {title}", "Mensagem importante.", "<p>Mensagem importante.</p>");

        var result = EmailTemplate.Render(message, "https://pethost.cfd");

        result.HtmlBody.Should().Contain($"https://pethost.cfd{EmailHeadingAssets.Images[title].Path}")
            .And.Contain($"alt=\"{System.Net.WebUtility.HtmlEncode(title)}\"")
            .And.Contain("border:2px solid #1F1A17;border-radius:24px")
            .And.Contain("padding:0 5px 5px 0")
            .And.NotContain("Um cantinho de confiança para quem faz parte da família.")
            .And.NotContain("box-shadow:");
        result.TextBody.Should().Contain("Mensagem importante.");
        result.Subject.Should().Be(message.Subject);
    }

    [Fact]
    public void Render_Should_KeepDynamicPaymentIssueOutOfTheStaticImageAddress()
    {
        var message = new EmailMessage("test@example.invalid", null, "PetHost — pagamento precisa de atenção (payout_blocked)", "Pendência: payout_blocked", "<p>Pendência: payout_blocked</p>");
        var result = EmailTemplate.Render(message, "https://pethost.cfd");
        result.HtmlBody.Should().Contain($"https://pethost.cfd{EmailHeadingAssets.Images["pagamento precisa de atenção"].Path}")
            .And.Contain("Pendência: payout_blocked</p>");
        result.Subject.Should().Be(message.Subject);
    }

    [Fact]
    public void Render_Should_KeepUnknownOrUnconfiguredHeadingsAsReadableText()
    {
        var message = new EmailMessage("test@example.invalid", null, "PetHost — título novo", "Texto", "<p>Texto</p>");
        EmailTemplate.Render(message, "https://pethost.cfd").HtmlBody.Should().Contain($">{System.Net.WebUtility.HtmlEncode("título novo")}</h1>").And.NotContain("/brand/email-headings/");
        var known = message with { Subject = "PetHost — redefinição de senha" };
        EmailTemplate.Render(known, null).HtmlBody.Should().Contain($">{System.Net.WebUtility.HtmlEncode("redefinição de senha")}</h1>").And.NotContain("<img");
    }

    [Fact]
    public void Render_Should_KeepTheContentAndProvideTheSameActionInBothAlternatives()
    {
        var input = new EmailMessage("test@example.invalid", "Ana", "PetHost — sua estadia",
            "Seu pet chegou.", "<p>Seu pet chegou.</p>", new EmailAction("Ver estadia", "/owner/bookings/123"));

        var result = EmailTemplate.Render(input, "https://app.example.test");

        result.Subject.Should().Be(input.Subject);
        result.ToAddress.Should().Be(input.ToAddress);
        result.HtmlBody.Should().Contain("<!doctype html>").And.Contain("lang=\"pt-BR\"")
            .And.Contain("#FEFAF3").And.Contain("#F26B3A").And.Contain("Seu pet chegou.</p>")
            .And.Contain("href=\"https://app.example.test/owner/bookings/123\"")
            .And.Contain("copie e cole este link");
        result.TextBody.Should().Contain("Seu pet chegou.").And.Contain("Ver estadia: https://app.example.test/owner/bookings/123");
        result.Action.Should().BeNull("o corpo final é persistido, não a instrução de apresentação");
    }

    [Fact]
    public void Render_Should_EncodeMetadataAndTokenQueryWithoutChangingTheTarget()
    {
        var input = new EmailMessage("test@example.invalid", null, "PetHost — <script>bad</script>", "texto", "<p>Seguro &amp; salvo.</p>",
            new EmailAction("Abrir <agora>", "https://app.example.test/reset-password?token=a%2Bb&next=home"));

        var result = EmailTemplate.Render(input, null);

        result.HtmlBody.Should().NotContain("<script>").And.Contain("&lt;script&gt;")
            .And.Contain("Abrir &lt;agora&gt;").And.Contain("token=a%2Bb&amp;next=home");
        result.TextBody.Should().Contain("token=a%2Bb&next=home");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("//evil.example.test/path")]
    [InlineData("/\\evil.example.test")]
    [InlineData("https://user:password@evil.example.test")]
    public void Render_Should_NotCreateLinksForUnsafeTargets(string target)
    {
        var input = new EmailMessage("test@example.invalid", null, "Aviso", "texto", "<p>Conteúdo</p>", new EmailAction("Abrir", target));

        var result = EmailTemplate.Render(input, "https://app.example.test");

        result.HtmlBody.Should().NotContain("href=");
        result.TextBody.Should().NotContain(target);
        result.HtmlBody.Should().Contain("Conteúdo");
    }

    [Fact]
    public void Render_Should_NotInventAnAddressWhenTheFrontendIsNotConfigured()
    {
        var input = new EmailMessage("test@example.invalid", null, "Aviso", "Código: abc", "<p>Código: abc</p>");

        var result = EmailTemplate.Render(input, null);

        result.HtmlBody.Should().NotContain("href=").And.Contain("Código: abc");
        result.TextBody.Should().Contain("Código: abc");
        result.HtmlBody.Should().NotContain("@font-face").And.NotContain("<img");
    }

    [Fact]
    public void Render_Should_UseTheFrontendBrandAssetsWithoutLeakingTheResetTokenToThem()
    {
        var message = new EmailMessage("test@example.invalid", null, "PetHost — senha", "texto", "<p>Abra o link.</p>",
            new EmailAction("Criar senha", "https://pethost.vercel.app/reset-password#token=secret"));

        var result = EmailTemplate.Render(message, "https://pethost.vercel.app");

        result.HtmlBody.Should().Contain("src=\"https://pethost.vercel.app/brand/email-logo.png?v=quintal-light-3\"")
            .And.Contain("alt=\"PetHost\"").And.Contain("font-family:'Caprasimo'")
            .And.Contain("font-family:'Atkinson Hyperlegible Next'")
            .And.Contain("https://pethost.vercel.app/fonts/caprasimo-latin-400-normal.woff2")
            .And.Contain("https://pethost.vercel.app/fonts/atkinson-hyperlegible-next-latin-400-normal.woff2")
            .And.Contain("https://pethost.vercel.app/fonts/atkinson-hyperlegible-next-latin-700-normal.woff2")
            .And.NotContain("email-logo.png#token").And.NotContain("woff2#token");
    }

    [Fact]
    public void Render_Should_KeepTheQuintalLightThemeAndOriginalDisplayFontInEveryDeviceTheme()
    {
        var message = new EmailMessage("test@example.invalid", null, "PetHost — redefinição de senha", "Texto", "<p>Texto legível.</p>", new EmailAction("Abrir", "/reset-password#token=secret"));
        var result = EmailTemplate.Render(message, "https://pethost.cfd");
        result.HtmlBody.Should().Contain("name=\"color-scheme\" content=\"light only\"")
            .And.NotContain("prefers-color-scheme:dark")
            .And.NotContain("[data-ogsc]")
            .And.Contain("class=\"email-card\"")
            .And.Contain("class=\"email-content\"")
            .And.Contain("class=\"email-muted\"")
            .And.Contain("class=\"email-link\"")
            .And.Contain("-webkit-text-fill-color:#1F1A17!important")
            .And.Contain("background-image:linear-gradient(#FEFAF3,#FEFAF3)")
            .And.NotContain("#FFFFFF")
            .And.NotContain("padding:8px 12px")
            .And.Contain(".png?v=quintal-light-3\"")
            .And.Contain("href=\"https://pethost.cfd/reset-password#token=secret\"");
        result.TextBody.Should().Contain("Texto").And.Contain("/reset-password#token=secret");
    }

    [Theory]
    [InlineData("https://app.example.test", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("", true)]
    [InlineData("javascript:bad", false)]
    [InlineData("https://user:password@example.test", false)]
    [InlineData("https://example.test/?token=secret", false)]
    [InlineData("https://example.test/#section", false)]
    public void Validate_Should_RejectMalformedFrontendConfiguration(string appUrl, bool succeeds)
    {
        var options = new EmailOptions { Host = "mailpit", FromAddress = "test@example.invalid", AppUrl = appUrl };

        new EmailOptionsValidator().Validate(null, options).Succeeded.Should().Be(succeeds);
    }
}
