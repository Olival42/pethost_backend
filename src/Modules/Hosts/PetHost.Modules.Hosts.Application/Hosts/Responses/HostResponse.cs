using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Hosts.Application.Hosts.Responses;

/// <summary>
/// Anfitrião verificado: o perfil (CPF, verificação, situação no Stripe) junto com os dados da
/// conta, lidos do Auth. Mesmo formato para o próprio anfitrião e para o admin.
/// </summary>
/// <param name="Id">Id do anfitrião (o que os outros módulos guardam).</param>
/// <param name="Cpf">Só dígitos. É o da própria pessoa (na empresa, o do representante).</param>
/// <param name="Company">Dados da empresa. Só pessoa jurídica.</param>
/// <param name="IsActive">Status do perfil. Anda junto com <c>user.isActive</c>.</param>
/// <param name="SuspendedAt">Quando o admin suspendeu. O motivo vem em <c>user.suspensionReason</c>.</param>
/// <param name="IdentityReleasedAt">Quando o admin liberou o CPF/CNPJ (disputa). Esse perfil não volta mais.</param>
/// <param name="StripeConnected">Já existe conta conectada no Stripe (onboarding começou).</param>
/// <param name="StripePayoutsEnabled">O Stripe liberou os repasses: o cantinho pode aparecer na busca.</param>
/// <param name="StripeStatus">Cadastro de recebimento: <c>not_started</c>, <c>in_progress</c>, <c>under_review</c>, <c>restricted</c> ou <c>enabled</c>.</param>
public sealed record HostResponse(
    Guid Id,
    Guid UserId,
    string PersonType,
    string Cpf,
    CompanyResponse? Company,
    DateTimeOffset VerifiedAt,
    bool IsActive,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset? IdentityReleasedAt,
    bool StripeConnected,
    bool StripePayoutsEnabled,
    string StripeStatus,
    DateTimeOffset CreatedAt,
    AccountSummary? User)
{
    public static HostResponse From(Host host, AccountSummary? user)
    {
        ArgumentNullException.ThrowIfNull(host);

        return new HostResponse(
            host.Id.Value,
            host.UserId,
            PersonTypeValues.ToWire(host.PersonType),
            host.Cpf.Value,
            CompanyResponse.From(host.Company),
            host.VerifiedAt,
            host.IsActive,
            host.SuspendedAt,
            host.IdentityReleasedAt,
            host.StripeAccountId is not null,
            host.StripePayoutsEnabled,
            StripeOnboardingStatusValues.ToWire(host.StripeOnboardingStatus),
            host.CreatedAt,
            user);
    }

    /// <summary>Monta a resposta buscando a conta no Auth.</summary>
    public static async Task<HostResponse> LoadAsync(Host host, IHostAccounts accounts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(accounts);

        var found = await accounts.GetByIdsAsync([host.UserId], cancellationToken).ConfigureAwait(false);
        return From(host, found.Count > 0 ? found[0] : null);
    }
}
