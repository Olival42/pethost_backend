namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// Em que pé está o cadastro de recebimento do anfitrião no Stripe Connect. Calculado do espelho
/// da conta conectada (<see cref="Host.StripeOnboardingStatus"/>); não é gravado.
/// </summary>
public enum StripeOnboardingStatus
{
    /// <summary>Ainda não abriu o cadastro: não há conta conectada.</summary>
    NotStarted,

    /// <summary>Conta criada, mas a pessoa não terminou de preencher (<c>details_submitted = false</c>).</summary>
    InProgress,

    /// <summary>Preencheu tudo e o Stripe está conferindo (documento, conta bancária).</summary>
    UnderReview,

    /// <summary>O Stripe bloqueou os repasses e pede algo (<c>disabled_reason</c> diz o quê).</summary>
    Restricted,

    /// <summary>
    /// O Stripe recusou a conta de vez (<c>disabled_reason</c> = <c>rejected.*</c>: fraude, termos,
    /// lista de restrição...). A pessoa não resolve sozinha: o admin decide o que fazer.
    /// </summary>
    Rejected,

    /// <summary>O Stripe liberou os repasses: o anfitrião pode receber e os cantinhos aparecem na busca.</summary>
    Enabled,
}
