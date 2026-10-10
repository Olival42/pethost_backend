using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// Perfil de anfitrião: o que só o anfitrião tem. Tem id próprio e aponta para a conta do
/// módulo Auth por <see cref="UserId"/> (1:1). Espelha a tabela <c>host.hosts</c>.
/// </summary>
/// <remarks>
/// O anfitrião é quem recebe (Stripe Connect), então aqui ficam os dados que a conta
/// conectada pede e que o Stripe não devolve por completo depois:
/// <list type="bullet">
/// <item><b>Pessoa física</b> (<c>business_type = individual</c>): o CPF do anfitrião.</item>
/// <item><b>Pessoa jurídica</b> (<c>business_type = company</c>): CNPJ, razão social, nome
/// fantasia e endereço da empresa, mais o CPF do <b>representante legal</b>, que o Stripe
/// exige de toda empresa. Nome, nascimento, telefone e endereço do representante são os da
/// conta (Auth).</item>
/// </list>
/// Por enquanto só a tabela existe: cadastro, edição e status entram com as rotas do
/// anfitrião.
/// </remarks>
public sealed class Host : Entity<HostId>
{
    public const int LegalNameMaxLength = 160;
    public const int TradeNameMaxLength = 120;

    /// <summary>Limite de <c>hosts.stripe_account_id</c> no dicionário de dados.</summary>
    public const int StripeAccountIdMaxLength = 255;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Host()
    {
        Cpf = null!;
    }

    private Host(Guid userId, PersonType personType, Cpf cpf, DateTimeOffset now)
        : base(HostId.New())
    {
        UserId = userId;
        PersonType = personType;
        Cpf = cpf;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>A conta do anfitrião no Auth. FK lógica, única: um anfitrião por conta.</summary>
    public Guid UserId { get; private set; }

    public PersonType PersonType { get; private set; }

    /// <summary>
    /// Pessoa física: CPF do anfitrião (único entre as pessoas físicas). Pessoa jurídica:
    /// CPF do representante legal — a mesma pessoa pode representar mais de uma empresa.
    /// </summary>
    public Cpf Cpf { get; private set; }

    /// <summary>Só pessoa jurídica. Único.</summary>
    public Cnpj? Cnpj { get; private set; }

    /// <summary>Razão social. Só pessoa jurídica (Stripe: <c>company.name</c>).</summary>
    public string? LegalName { get; private set; }

    /// <summary>Nome fantasia: o nome que o tutor vê. Só pessoa jurídica.</summary>
    public string? TradeName { get; private set; }

    /// <summary>Endereço da empresa. Só pessoa jurídica.</summary>
    public CompanyAddress? CompanyAddress { get; private set; }

    /// <summary>Conta conectada no Stripe (<c>acct_...</c>). Nula até o onboarding.</summary>
    public string? StripeAccountId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Anfitrião pessoa física da conta <paramref name="userId"/>.</summary>
    public static Host CreateIndividual(Guid userId, Cpf cpf, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cpf);
        EnsureAccount(userId);

        return new Host(userId, PersonType.Individual, cpf, now);
    }

    /// <summary>
    /// Anfitrião pessoa jurídica. Razão social e nome fantasia são obrigatórios; todos os
    /// erros voltam juntos.
    /// </summary>
    public static Result<Host> CreateCompany(
        Guid userId,
        Cpf representativeCpf,
        Cnpj cnpj,
        string? legalName,
        string? tradeName,
        CompanyAddress companyAddress,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(representativeCpf);
        ArgumentNullException.ThrowIfNull(cnpj);
        ArgumentNullException.ThrowIfNull(companyAddress);
        EnsureAccount(userId);

        var errors = new List<Error>();
        var normalizedLegalName = Required(legalName, LegalNameMaxLength, HostsErrors.LegalNameRequired, HostsErrors.LegalNameTooLong, errors);
        var normalizedTradeName = Required(tradeName, TradeNameMaxLength, HostsErrors.TradeNameRequired, HostsErrors.TradeNameTooLong, errors);

        if (errors.Count > 0)
            return Result<Host>.Failure(errors);

        var host = new Host(userId, PersonType.Company, representativeCpf, now)
        {
            Cnpj = cnpj,
            LegalName = normalizedLegalName,
            TradeName = normalizedTradeName,
            CompanyAddress = companyAddress,
        };

        return Result<Host>.Success(host);
    }

    private static void EnsureAccount(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("The host must point to an account.", nameof(userId));
    }

    private static string? Required(string? value, int maxLength, Error required, Error tooLong, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(required);
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            errors.Add(tooLong);

        return trimmed;
    }
}
