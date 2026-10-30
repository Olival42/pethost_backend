namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>O básico de um pet pedido na reserva, lido do módulo Pets.</summary>
/// <param name="OwnerId">Tutor dono (<c>owner.owners.id</c>). Nulo quando é pet de anfitrião.</param>
/// <param name="Species">Valor de <c>pets.species</c>.</param>
/// <param name="Size">Valor de <c>pets.size</c>, quando tem.</param>
public sealed record BookablePet(Guid Id, Guid? OwnerId, string Name, string Species, string? Size, bool IsActive);
