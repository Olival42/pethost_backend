namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// Quem é o anfitrião perante a Receita e o Stripe (<c>business_type</c> da conta
/// conectada): uma pessoa física ou uma empresa.
/// </summary>
public enum PersonType
{
    /// <summary>Pessoa física: CPF do próprio anfitrião.</summary>
    Individual,

    /// <summary>Pessoa jurídica: CNPJ, razão social, nome fantasia e endereço da empresa.</summary>
    Company,
}
