using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Domain.Owners;

/// <summary>
/// Perfil de tutor: o que só o tutor tem. Tem id próprio e aponta para a conta do
/// módulo Auth por <see cref="UserId"/> (1:1). Espelha a tabela <c>owner.owners</c>.
/// </summary>
/// <remarks>
/// O tutor é quem paga, então aqui ficam os dados que o Stripe usa para identificar
/// o pagador: o CPF (tax ID <c>br_cpf</c> do Customer) e o id do Customer criado lá.
/// Nome, e-mail, telefone e endereço são da conta e vivem no Auth.
/// <para>
/// O CPF pode ser corrigido <b>até o primeiro pagamento</b>. Depois dele fica travado:
/// já foi para o Customer do Stripe e está nos pagamentos feitos. O marco é o
/// <see cref="StripeCustomerId"/>, criado justamente no primeiro pagamento.
/// </para>
/// <para>
/// O perfil tem status próprio (<see cref="IsActive"/>), que anda junto com o da conta:
/// o caso de uso inativa e reativa os dois.
/// </para>
/// </remarks>
public sealed class Owner : Entity<OwnerId>
{
    /// <summary>Limite de <c>owners.stripe_customer_id</c> no dicionário de dados.</summary>
    public const int StripeCustomerIdMaxLength = 255;

    /// <summary>Limite do motivo informado pelo admin (suspensão, liberação do CPF).</summary>
    public const int AdminReasonMaxLength = 500;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Owner()
    {
    }

    private Owner(Guid userId, Cpf cpf, DateTimeOffset now)
        : base(OwnerId.New())
    {
        UserId = userId;
        Cpf = cpf;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// A conta do tutor no módulo Auth. Referência (FK lógica), não chave: uma conta tem
    /// no máximo um perfil de tutor, garantido por índice único.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Único entre os tutores: um CPF, uma conta de tutor. Travado após o primeiro
    /// pagamento. Nulo só num tutor suspenso cujo CPF o admin liberou (disputa de CPF).
    /// </summary>
    public Cpf? Cpf { get; private set; }

    /// <summary>
    /// Customer no Stripe (<c>cus_...</c>). Nulo até o primeiro pagamento, quando o
    /// módulo de pagamentos cria o Customer e chama <see cref="LinkStripeCustomer"/>.
    /// </summary>
    public string? StripeCustomerId { get; private set; }

    /// <summary>Falso depois de inativado. Nasce ativo.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Quando o admin suspendeu o tutor. Anda junto com a suspensão da conta (Auth).
    /// Diferente de inativar: o tutor não tira a suspensão sozinho.
    /// </summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    public bool IsSuspended => SuspendedAt is not null;

    /// <summary>Quando foi inativado pela última vez. Nulo enquanto ativo.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Cria o perfil de tutor da conta <paramref name="userId"/>, com id novo.</summary>
    public static Owner Create(Guid userId, Cpf cpf, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cpf);

        if (userId == Guid.Empty)
            throw new ArgumentException("The owner must point to an account.", nameof(userId));

        return new Owner(userId, cpf, now);
    }

    /// <summary>
    /// Já houve pagamento. Marco para travar os dados que identificam o pagador no
    /// Stripe e na verificação de documentos: CPF e data de nascimento.
    /// </summary>
    public bool HasPaid => StripeCustomerId is not null;

    /// <summary>O CPF ainda pode ser trocado: nenhum pagamento foi feito.</summary>
    public bool CanChangeCpf => !HasPaid;

    /// <summary>
    /// A data de nascimento (que fica na conta, no Auth) ainda pode ser trocada: nenhum
    /// pagamento foi feito. Quem aplica a regra é o caso de uso.
    /// </summary>
    public bool CanChangeBirthDate => !HasPaid;

    /// <summary>
    /// Troca o CPF. Recusa depois do primeiro pagamento. A unicidade entre tutores é
    /// conferida pelo caso de uso, que enxerga os outros tutores.
    /// </summary>
    public Result ChangeCpf(Cpf cpf, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cpf);

        if (!CanChangeCpf)
            return Result.Failure(OwnersErrors.CpfLocked);

        if (cpf == Cpf)
            return Result.Success();

        Cpf = cpf;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Suspende o tutor (ação do admin). Idempotente: vale a primeira suspensão.</summary>
    public void Suspend(DateTimeOffset now)
    {
        if (IsSuspended)
            return;

        SuspendedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Tira a suspensão. Recusa se o CPF foi liberado: aquela conta era indevida, e o CPF
    /// já pode estar com o dono de verdade.
    /// </summary>
    public Result LiftSuspension(DateTimeOffset now)
    {
        if (Cpf is null)
            return Result.Failure(OwnersErrors.CpfReleased);

        if (!IsSuspended)
            return Result.Success();

        SuspendedAt = null;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Libera o CPF (disputa: alguém usou o CPF de outra pessoa). Só de tutor suspenso:
    /// primeiro o admin tira a conta do ar, depois devolve o CPF para quem é dono.
    /// </summary>
    public Result ReleaseCpf(DateTimeOffset now)
    {
        if (!IsSuspended)
            return Result.Failure(OwnersErrors.NotSuspended);

        if (Cpf is null)
            return Result.Success();

        Cpf = null;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Inativa o perfil. Idempotente: inativar um perfil inativo não muda nada.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return;

        IsActive = false;
        DeactivatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Reativa o perfil. Idempotente: reativar um perfil ativo não muda nada.</summary>
    public void Reactivate(DateTimeOffset now)
    {
        if (IsActive)
            return;

        IsActive = true;
        DeactivatedAt = null;
        UpdatedAt = now;
    }

    /// <summary>Guarda o Customer do Stripe. A partir daqui o CPF fica travado.</summary>
    public void LinkStripeCustomer(string stripeCustomerId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeCustomerId);

        StripeCustomerId = stripeCustomerId;
        UpdatedAt = now;
    }
}
