namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>O que uma sincronização com o Stripe mudou na liberação dos repasses.</summary>
public enum StripePayoutsChange
{
    /// <summary>Continua como estava.</summary>
    None,

    /// <summary>Passou a poder receber: os cantinhos entram na busca.</summary>
    Enabled,

    /// <summary>Deixou de poder receber: os cantinhos saem da busca.</summary>
    Disabled,
}
