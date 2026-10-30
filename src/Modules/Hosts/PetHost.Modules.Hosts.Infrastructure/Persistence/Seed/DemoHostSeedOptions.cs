namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Seed;

/// <summary>
/// Anfitrião de demonstração (apresentação do projeto): criado na subida, já verificado,
/// opcionalmente ligado a uma conta conectada do Stripe. Desligado por padrão. Vem do
/// ambiente como <c>Seed__DemoHost__Enabled</c> etc.
/// </summary>
/// <remarks>Use só dados fictícios: estes valores ficam no <c>.env</c>, em texto.</remarks>
public sealed class DemoHostSeedOptions
{
    public const string SectionName = "Seed:DemoHost";

    public bool Enabled { get; init; }

    public string FullName { get; init; } = "Dona Cida (demonstração)";

    public string? Email { get; init; }

    /// <summary>Precisa seguir a regra de senha forte do cadastro. Nunca versionada.</summary>
    public string? Password { get; init; }

    public string Phone { get; init; } = "44999990000";

    /// <summary><c>yyyy-MM-dd</c>, 18+.</summary>
    public string BirthDate { get; init; } = "1968-03-15";

    /// <summary>CPF fictício, com dígitos verificadores válidos.</summary>
    public string? Cpf { get; init; }

    public string ZipCode { get; init; } = "87033000";

    public string Street { get; init; } = "Rua das Palmeiras";

    public string Number { get; init; } = "100";

    public string Neighborhood { get; init; } = "Jardim Alvorada";

    public string City { get; init; } = "Maringá";

    public string State { get; init; } = "PR";

    /// <summary>
    /// Conta conectada do Stripe (<c>acct_...</c>) criada à mão no Dashboard (modo de teste ou
    /// produção). Vazio = o anfitrião fica sem Stripe, e o onboarding segue pelo app.
    /// </summary>
    public string? StripeAccountId { get; init; }
}
