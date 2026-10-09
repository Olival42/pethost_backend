using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Owners.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Owners. Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class OwnersErrors
{
    // --- Validação de formato (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error CpfInvalid =
        Error.Validation("cpf", "CPF is invalid.");

    /// <summary>Trocar CPF ou data de nascimento é dado sensível: pede a senha da conta.</summary>
    public static readonly Error CurrentPasswordRequired =
        Error.Validation("currentPassword", "Current password is required to change the CPF or the birth date.");

    /// <summary>400 no campo, e não 401: o token é válido, quem errou foi a senha digitada.</summary>
    public static readonly Error CurrentPasswordIncorrect =
        Error.Validation("currentPassword", "Current password is incorrect.");

    // --- Regras de negócio ---

    /// <summary>
    /// 409: o CPF já pertence a outra conta de tutor. A mensagem orienta quem é o dono do
    /// CPF a procurar o suporte: pode ser alguém usando o CPF dele.
    /// </summary>
    public static readonly Error CpfAlreadyRegistered =
        new("OWNER_CPF_ALREADY_REGISTERED", "An owner account with this CPF already exists. If this CPF is yours, contact support.");

    /// <summary>
    /// 422: o CPF não pode mais ser trocado — o tutor já pagou ao menos uma vez e o
    /// CPF está no Customer do Stripe e nos pagamentos.
    /// </summary>
    public static readonly Error CpfLocked =
        new("OWNER_CPF_LOCKED", "The CPF cannot be changed after the first payment.");

    /// <summary>
    /// 422: a data de nascimento não pode mais ser trocada — o tutor já pagou e ela foi
    /// usada para identificá-lo.
    /// </summary>
    public static readonly Error BirthDateLocked =
        new("OWNER_BIRTH_DATE_LOCKED", "The birth date cannot be changed after the first payment.");

    /// <summary>422: liberar o CPF só de tutor suspenso.</summary>
    public static readonly Error NotSuspended =
        new("OWNER_SUSPENSION_REQUIRED", "The CPF can only be released from a suspended owner.");

    /// <summary>422: o CPF deste tutor foi liberado; a suspensão não sai mais.</summary>
    public static readonly Error CpfReleased =
        new("OWNER_CPF_RELEASED", "This owner's CPF was released, so the suspension cannot be lifted.");

    public static readonly Error ReasonRequired =
        Error.Validation("reason", "A reason is required.");

    public static readonly Error ReasonTooLong =
        Error.Validation("reason", $"Reason must be at most {Owner.AdminReasonMaxLength} characters.");

    /// <summary>404: não existe tutor com esse id.</summary>
    public static Error NotFound(OwnerId id) =>
        new("OWNER_NOT_FOUND", $"Owner '{id.Value}' was not found.");

    /// <summary>404: a conta do token não tem perfil de tutor (conta antiga, de antes do cadastro unificado).</summary>
    public static readonly Error ProfileNotFound =
        new("OWNER_NOT_FOUND", "This account has no owner profile yet.");
}
