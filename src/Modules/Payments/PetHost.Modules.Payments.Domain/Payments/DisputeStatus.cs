namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Contestação (chargeback) da cobrança no cartão. Pix não tem contestação no Stripe.</summary>
public enum DisputeStatus
{
    /// <summary>Sem contestação.</summary>
    None,

    /// <summary>O tutor contestou e o banco ainda não decidiu. O repasse fica retido.</summary>
    Open,

    /// <summary>Decidida a favor do PetHost: segue a regra normal.</summary>
    Won,

    /// <summary>Decidida a favor do tutor: o dinheiro voltou para ele e o anfitrião não recebe (ou devolve) a parte dele.</summary>
    Lost,
}
