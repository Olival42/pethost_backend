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
/// <para>
/// Só existe anfitrião <b>verificado</b>: o perfil nasce quando o admin aprova a solicitação
/// de cadastro (<c>HostApplication.Approve</c>), com os dados conferidos nela.
/// </para>
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

    private Host(Guid userId, PersonType personType, Cpf cpf, Guid applicationId, Guid verifiedBy, DateTimeOffset now)
        : base(HostId.New())
    {
        UserId = userId;
        PersonType = personType;
        Cpf = cpf;
        ApplicationId = applicationId;
        VerifiedBy = verifiedBy;
        VerifiedAt = now;
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

    /// <summary>Os dados da empresa juntos, ou <c>null</c> na pessoa física.</summary>
    public CompanyDetails? Company =>
        PersonType == PersonType.Company && Cnpj is not null && LegalName is not null && TradeName is not null && CompanyAddress is not null
            ? CompanyDetails.Restore(Cnpj, LegalName, TradeName, CompanyAddress)
            : null;

    /// <summary>Conta conectada no Stripe (<c>acct_...</c>). Nula até o onboarding.</summary>
    public string? StripeAccountId { get; private set; }

    /// <summary>A solicitação aprovada que criou este perfil (host_applications.id).</summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>Quando o admin aprovou a identidade.</summary>
    public DateTimeOffset VerifiedAt { get; private set; }

    /// <summary>O admin que aprovou (users.id). Sem FK, como <c>suspended_by</c>.</summary>
    public Guid VerifiedBy { get; private set; }

    // --- Espelho da conta conectada no Stripe (webhook account.updated). Quem grava é o
    //     módulo de pagamentos, quando existir; por enquanto ficam no padrão. ---

    /// <summary>A pessoa terminou o onboarding do Stripe (<c>details_submitted</c>).</summary>
    public bool StripeDetailsSubmitted { get; private set; }

    /// <summary>A conta conectada pode receber cobranças (<c>charges_enabled</c>).</summary>
    public bool StripeChargesEnabled { get; private set; }

    /// <summary>
    /// A conta conectada pode receber repasses (<c>payouts_enabled</c>). Cantinho só aparece na
    /// busca com isto ligado.
    /// </summary>
    public bool StripePayoutsEnabled { get; private set; }

    /// <summary>Por que o Stripe bloqueou a conta (<c>requirements.disabled_reason</c>), para o painel.</summary>
    public string? StripeDisabledReason { get; private set; }

    /// <summary>Quando a conta passou a poder receber repasses pela primeira vez.</summary>
    public DateTimeOffset? StripeOnboardedAt { get; private set; }

    /// <summary>Última sincronização com o Stripe.</summary>
    public DateTimeOffset? StripeSyncedAt { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>
    /// Quando o admin suspendeu. Anda junto com a suspensão da conta (Auth). Diferente de
    /// inativar: o anfitrião não tira a suspensão sozinho.
    /// </summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    public bool IsSuspended => SuspendedAt is not null;

    /// <summary>
    /// Quando o admin liberou o CPF/CNPJ deste anfitrião (disputa: alguém usou o documento de
    /// outra pessoa ou empresa). Os valores ficam guardados como evidência, mas deixam de contar
    /// nas regras de unicidade, e a suspensão não sai mais.
    /// </summary>
    public DateTimeOffset? IdentityReleasedAt { get; private set; }

    public bool IsIdentityReleased => IdentityReleasedAt is not null;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Anfitrião pessoa física da conta <paramref name="userId"/>, verificado na solicitação
    /// <paramref name="applicationId"/> pelo admin <paramref name="verifiedBy"/>.
    /// </summary>
    public static Host CreateIndividual(Guid userId, Cpf cpf, Guid applicationId, Guid verifiedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cpf);
        EnsureAccount(userId);
        EnsureVerification(applicationId, verifiedBy);

        return new Host(userId, PersonType.Individual, cpf, applicationId, verifiedBy, now);
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
        Guid applicationId,
        Guid verifiedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(representativeCpf);
        ArgumentNullException.ThrowIfNull(cnpj);
        ArgumentNullException.ThrowIfNull(companyAddress);
        EnsureAccount(userId);
        EnsureVerification(applicationId, verifiedBy);

        var errors = new List<Error>();
        var normalizedLegalName = Required(legalName, LegalNameMaxLength, HostsErrors.LegalNameRequired, HostsErrors.LegalNameTooLong, errors);
        var normalizedTradeName = Required(tradeName, TradeNameMaxLength, HostsErrors.TradeNameRequired, HostsErrors.TradeNameTooLong, errors);

        if (errors.Count > 0)
            return Result<Host>.Failure(errors);

        var host = new Host(userId, PersonType.Company, representativeCpf, applicationId, verifiedBy, now)
        {
            Cnpj = cnpj,
            LegalName = normalizedLegalName,
            TradeName = normalizedTradeName,
            CompanyAddress = companyAddress,
        };

        return Result<Host>.Success(host);
    }

    /// <summary>
    /// Liga a conta conectada do Stripe (<c>acct_...</c>), criada no onboarding. Os status
    /// (<see cref="StripePayoutsEnabled"/>...) chegam depois, pelo webhook.
    /// </summary>
    /// <summary>
    /// Troca o que a empresa pode mudar sem nova verificação: nome fantasia e endereço da
    /// empresa (<c>null</c> = não mexer). CNPJ e razão social são identidade: travados.
    /// </summary>
    public Result ChangeCompanyContact(string? tradeName, CompanyAddress? address, DateTimeOffset now)
    {
        if (PersonType != PersonType.Company)
            return Result.Failure(HostsErrors.NotACompany);

        var errors = new List<Error>();
        var normalizedTradeName = tradeName is null
            ? TradeName
            : Required(tradeName, TradeNameMaxLength, HostsErrors.TradeNameRequired, HostsErrors.TradeNameTooLong, errors);

        if (errors.Count > 0)
            return Result.Failure(errors);

        var changed = false;

        if (normalizedTradeName != TradeName)
        {
            TradeName = normalizedTradeName;
            changed = true;
        }

        if (address is not null && address != CompanyAddress)
        {
            CompanyAddress = address;
            changed = true;
        }

        if (changed)
            UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Inativa o perfil (decisão do anfitrião). Idempotente.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return;

        IsActive = false;
        DeactivatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Reativa o perfil. Idempotente.</summary>
    public void Reactivate(DateTimeOffset now)
    {
        if (IsActive)
            return;

        IsActive = true;
        DeactivatedAt = null;
        UpdatedAt = now;
    }

    /// <summary>Suspende (ação do admin). Idempotente: vale a primeira suspensão.</summary>
    public void Suspend(DateTimeOffset now)
    {
        if (IsSuspended)
            return;

        SuspendedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Tira a suspensão. Recusa se o CPF/CNPJ foi liberado: aquele perfil era indevido.</summary>
    public Result LiftSuspension(DateTimeOffset now)
    {
        if (IsIdentityReleased)
            return Result.Failure(HostsErrors.IdentityReleased);

        if (!IsSuspended)
            return Result.Success();

        SuspendedAt = null;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Libera o CPF (pessoa física) ou o CNPJ e o CPF do representante (empresa) para o dono de
    /// verdade. Só de anfitrião suspenso; idempotente.
    /// </summary>
    public Result ReleaseIdentity(DateTimeOffset now)
    {
        if (!IsSuspended)
            return Result.Failure(HostsErrors.SuspensionRequired);

        if (IsIdentityReleased)
            return Result.Success();

        IdentityReleasedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    public void LinkStripeAccount(string stripeAccountId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeAccountId);

        var trimmed = stripeAccountId.Trim();
        if (trimmed.Length > StripeAccountIdMaxLength)
            throw new ArgumentException($"Stripe account id must be at most {StripeAccountIdMaxLength} characters.", nameof(stripeAccountId));

        StripeAccountId = trimmed;
        UpdatedAt = now;
    }

    private static void EnsureAccount(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("The host must point to an account.", nameof(userId));
    }

    private static void EnsureVerification(Guid applicationId, Guid verifiedBy)
    {
        if (applicationId == Guid.Empty)
            throw new ArgumentException("The host must come from an approved application.", nameof(applicationId));

        if (verifiedBy == Guid.Empty)
            throw new ArgumentException("The host must be verified by an admin.", nameof(verifiedBy));
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
