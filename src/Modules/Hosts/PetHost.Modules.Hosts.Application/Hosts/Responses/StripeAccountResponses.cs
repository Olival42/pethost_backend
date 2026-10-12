using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Application.Hosts.Responses;

/// <summary>"Meus recebimentos": em que pé está o cadastro de recebimento no Stripe.</summary>
/// <param name="Status">
/// <c>not_started</c> (sem conta conectada), <c>in_progress</c> (falta terminar o cadastro),
/// <c>under_review</c> (o Stripe está conferindo), <c>restricted</c> (o Stripe pede algo — ver
/// <paramref name="DisabledReason"/>) ou <c>enabled</c> (pode receber).
/// </param>
/// <param name="DisabledReason">O que o Stripe diz que falta (<c>requirements.past_due</c>...).</param>
/// <param name="OnboardedAt">Quando os repasses foram liberados pela primeira vez.</param>
/// <param name="SyncedAt">Última leitura da conta no Stripe.</param>
/// <param name="ListingsVisible">Os cantinhos aparecem na busca: ativo, sem suspensão e com repasses liberados.</param>
public sealed record StripeAccountResponse(
    string Status,
    bool DetailsSubmitted,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    string? DisabledReason,
    DateTimeOffset? OnboardedAt,
    DateTimeOffset? SyncedAt,
    bool ListingsVisible)
{
    public static StripeAccountResponse From(Host host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return new StripeAccountResponse(
            StripeOnboardingStatusValues.ToWire(host.StripeOnboardingStatus),
            host.StripeDetailsSubmitted,
            host.StripeChargesEnabled,
            host.StripePayoutsEnabled,
            host.StripeDisabledReason,
            host.StripeOnboardedAt,
            host.StripeSyncedAt,
            host.IsListable);
    }
}

/// <summary>Sessão do onboarding embutido: o app inicia o componente do Stripe com estes dados.</summary>
/// <param name="ClientSecret">Segredo de uso único da sessão. Não guardar nem logar.</param>
/// <param name="PublishableKey">Chave publicável da plataforma (<c>pk_test_...</c>).</param>
/// <param name="ExpiresAt">Quando a sessão vence, em Unix time (segundos, UTC). Vencida: peça outra.</param>
public sealed record StripeOnboardingSessionResponse(
    string ClientSecret,
    string PublishableKey,
    long ExpiresAt,
    StripeAccountResponse Account);

/// <summary>Link do onboarding hospedado pelo Stripe: o app abre a URL; no fim o Stripe devolve para o app.</summary>
/// <param name="Url">Uso único. Não guardar: peça outro a cada vez.</param>
/// <param name="ExpiresAt">Quando o link vence, em Unix time (segundos, UTC).</param>
public sealed record StripeOnboardingLinkResponse(string Url, long ExpiresAt, StripeAccountResponse Account);
