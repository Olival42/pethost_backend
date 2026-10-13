namespace PetHost.Shared.Contracts.Owners;

/// <summary>
/// Contrato público de leitura do módulo Owners (§5). Outro módulo que guarda o id do
/// tutor — o Pets, no dono do pet — usa isto para achar o tutor da conta logada e para
/// conferir tutores, sem referenciar o Owners nem fazer JOIN entre schemas.
/// </summary>
public interface IOwnerDirectory
{
    /// <summary>O tutor da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de tutor.</summary>
    Task<OwnerReference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tutores pelos ids, numa consulta só. Id que não existe não aparece.</summary>
    Task<IReadOnlyList<OwnerReference>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken);
}

/// <summary>Referência a um tutor: o id dele e o da conta. Sem CPF nem outro dado pessoal.</summary>
/// <param name="IsSuspended">Suspenso pelo admin. Diferente de inativo: só o admin tira.</param>
public sealed record OwnerReference(Guid Id, Guid UserId, bool IsActive, bool IsSuspended = false)
{
    /// <summary>Ativo e sem suspensão: pode usar a conta (pedir estadia).</summary>
    public bool IsAvailable => IsActive && !IsSuspended;
}
