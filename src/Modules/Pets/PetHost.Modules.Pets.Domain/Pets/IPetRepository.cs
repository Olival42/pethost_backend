namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>Acesso a <see cref="Pet"/>. A Application só conhece esta interface (§11).</summary>
public interface IPetRepository
{
    /// <summary>Pelo id. Rastreado: quem chama pode alterar e gravar.</summary>
    Task<Pet?> GetByIdAsync(PetId id, CancellationToken cancellationToken);

    /// <summary>
    /// Todos os pets de um tutor ou anfitrião, ativos e inativos, do mais novo para o mais
    /// antigo. Sem paginação: são poucos por pessoa.
    /// </summary>
    Task<IReadOnlyList<Pet>> ListByKeeperAsync(PetKeeper keeper, CancellationToken cancellationToken);

    /// <summary>Só os pets ativos de um tutor ou anfitrião (o que os outros podem ver).</summary>
    Task<IReadOnlyList<Pet>> ListActiveByKeeperAsync(PetKeeper keeper, CancellationToken cancellationToken);

    /// <summary>
    /// Se algum pet <b>ativo</b>, fora <paramref name="exceptPetId"/>, já tem esse microchip.
    /// Pet desativado não conta: o animal pode ter mudado de tutor.
    /// </summary>
    Task<bool> MicrochipInUseAsync(string microchip, PetId? exceptPetId, CancellationToken cancellationToken);

    void Add(Pet pet);
}
