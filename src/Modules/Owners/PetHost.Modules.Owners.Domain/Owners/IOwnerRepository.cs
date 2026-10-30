namespace PetHost.Modules.Owners.Domain.Owners;

/// <summary>Acesso a <see cref="Owner"/>. A Application só conhece esta interface (§11).</summary>
public interface IOwnerRepository
{
    Task<bool> ExistsByCpfAsync(Cpf cpf, CancellationToken cancellationToken);

    /// <summary>Pelo id do tutor (rotas do admin). Rastreado.</summary>
    Task<Owner?> GetByIdAsync(OwnerId id, CancellationToken cancellationToken);

    /// <summary>Pelo id da conta (rotas <c>me</c>, o id vem do token). Rastreado.</summary>
    Task<Owner?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Todos os tutores, do mais novo para o mais antigo. Sem paginação: lista
    /// administrativa pequena (§13).
    /// </summary>
    Task<IReadOnlyList<Owner>> ListAsync(CancellationToken cancellationToken);

    void Add(Owner owner);
}
