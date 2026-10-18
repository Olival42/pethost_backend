namespace PetHost.Modules.Owners.Application.Abstractions;

/// <summary>
/// Porta para a exclusão da conta nos outros módulos (pets, conversas): a Infrastructure implementa
/// pelo contrato <c>IPersonalDataErasure</c> (§5).
/// </summary>
public interface IOwnerDataErasure
{
    /// <summary>Cada módulo apaga ou anonimiza a parte dele do tutor. Idempotente; erro de infraestrutura lança.</summary>
    Task EraseAsync(Guid userId, Guid ownerId, CancellationToken cancellationToken);
}
