using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Application.Abstractions;

/// <summary>
/// Porta para os pets (módulo Pets). §5: a Application declara a interface; a Infrastructure
/// implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IBookingPets
{
    /// <summary>Os pets pelos ids, ativos ou não, numa consulta só. Id que não existe não aparece.</summary>
    Task<IReadOnlyList<BookablePet>> GetAsync(IReadOnlyCollection<Guid> petIds, CancellationToken cancellationToken);
}
