namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>
/// Porta para a exclusão da conta nos outros módulos (pets, cantinhos, conversas): a Infrastructure
/// implementa pelo contrato <c>IPersonalDataErasure</c> (§5).
/// </summary>
public interface IHostDataErasure
{
    /// <summary>
    /// Cada módulo apaga ou anonimiza a parte dele do anfitrião. <paramref name="hostId"/> é nulo
    /// para conta ainda não aprovada. Idempotente; erro de infraestrutura lança.
    /// </summary>
    Task EraseAsync(Guid userId, Guid? hostId, CancellationToken cancellationToken);
}
