using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>
/// Porta para o Stripe Connect: a conta conectada do anfitrião (onde caem os repasses) e o
/// cadastro de recebimento dele. A Infrastructure implementa com o SDK do Stripe.
/// </summary>
/// <remarks>
/// Falhas esperadas voltam como <c>Result</c>: Stripe fora do ar ou sem chave configurada →
/// <c>HOST_STRIPE_UNAVAILABLE</c> (503); dado recusado pelo Stripe → <c>HOST_STRIPE_REQUEST_REJECTED</c> (422).
/// </remarks>
public interface IStripeConnect
{
    /// <summary>
    /// Cria a conta conectada, já preenchida com o que o PetHost conferiu na verificação, e
    /// devolve o id (<c>acct_...</c>). Repetir para o mesmo anfitrião em 24 h devolve a mesma
    /// conta (chave de idempotência pelo id do anfitrião).
    /// </summary>
    Task<Result<string>> CreateAccountAsync(StripeAccountDraft draft, CancellationToken cancellationToken);

    /// <summary>Relê a conta conectada na API do Stripe.</summary>
    Task<Result<StripeAccountSnapshot>> GetAccountAsync(string stripeAccountId, CancellationToken cancellationToken);

    /// <summary>Sessão do onboarding <b>embutido</b> (componente do Stripe dentro do app).</summary>
    Task<Result<StripeOnboardingSession>> CreateOnboardingSessionAsync(string stripeAccountId, CancellationToken cancellationToken);

    /// <summary>Link do onboarding <b>hospedado</b> (página do Stripe, que devolve para o app).</summary>
    Task<Result<StripeOnboardingLink>> CreateOnboardingLinkAsync(string stripeAccountId, CancellationToken cancellationToken);
}

/// <summary>
/// O que o PetHost já sabe do anfitrião e manda para o Stripe na criação da conta, para a pessoa
/// não digitar de novo. O que faltar (conta bancária, documento, sócios) o Stripe pede no
/// onboarding dele.
/// </summary>
/// <param name="HostId">Vai nos metadados da conta e na chave de idempotência.</param>
/// <param name="Cpf">Pessoa física: o do anfitrião. Empresa: o do representante legal.</param>
/// <param name="Cnpj">Só empresa.</param>
/// <param name="LegalName">Razão social. Só empresa.</param>
/// <param name="TradeName">Nome fantasia. Só empresa.</param>
/// <param name="CompanyAddress">Endereço da empresa. Só empresa.</param>
/// <param name="Attempt">
/// Quantas contas recusadas o admin já desligou. Entra na chave de idempotência: depois do reset,
/// o Stripe cria uma conta nova em vez de devolver a recusada.
/// </param>
public sealed record StripeAccountDraft(
    Guid HostId,
    Guid UserId,
    PersonType PersonType,
    string Email,
    string FullName,
    DateOnly? BirthDate,
    string? Phone,
    AddressData? Address,
    string Cpf,
    string? Cnpj = null,
    string? LegalName = null,
    string? TradeName = null,
    AddressData? CompanyAddress = null,
    int Attempt = 0);

/// <summary>Sessão do componente de onboarding embutido.</summary>
/// <param name="ClientSecret">Vai para o app, que inicia o componente com ele. Uso único, curto.</param>
/// <param name="PublishableKey">Chave publicável da plataforma, que o componente também pede.</param>
public sealed record StripeOnboardingSession(string ClientSecret, string PublishableKey, DateTimeOffset ExpiresAt);

/// <summary>Link do onboarding hospedado pelo Stripe. Uso único, vence em poucos minutos.</summary>
public sealed record StripeOnboardingLink(string Url, DateTimeOffset ExpiresAt);
