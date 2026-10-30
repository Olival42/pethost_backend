namespace PetHost.Modules.Hosts.Domain.IdentityBlocks;

/// <summary>Acesso a <see cref="IdentityBlock"/>. A Application só conhece esta interface (§11).</summary>
public interface IIdentityBlockRepository
{
    /// <summary>O bloqueio dessa impressão de CPF, rastreado, ou <c>null</c>.</summary>
    Task<IdentityBlock?> FindAsync(string cpfFingerprint, CancellationToken cancellationToken);

    void Add(IdentityBlock block);

    void Remove(IdentityBlock block);
}
