namespace PetHost.Shared.Contracts.Pets;

/// <summary>
/// Contrato público de leitura do módulo Pets (§5). Outro módulo que guarda o id do pet — o
/// Bookings, nos pets da reserva — usa isto para conferir de quem é o pet, a espécie e o porte,
/// sem referenciar o Pets nem fazer JOIN entre schemas.
/// </summary>
public interface IPetDirectory
{
    /// <summary>Pets pelos ids, ativos ou não, numa consulta só. Id que não existe não aparece.</summary>
    Task<IReadOnlyList<PetReference>> GetByIdsAsync(
        IReadOnlyCollection<Guid> petIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// O básico de um pet para a reserva e para o "Combina com seu pet" (o Listings). Sem a ficha de
/// saúde nem as observações.
/// </summary>
/// <param name="OwnerId">Tutor dono (<c>owner.owners.id</c>). Nulo quando o pet é de um anfitrião.</param>
/// <param name="Species">Valor de <c>pets.species</c> (<c>dog</c>, <c>cat</c>...).</param>
/// <param name="Size">Valor de <c>pets.size</c> (<c>small</c>, <c>medium</c>, <c>large</c>), quando tem.</param>
/// <param name="GoodWithDogs">Convive bem com cães (<c>pets.good_with_dogs</c>).</param>
/// <param name="GoodWithCats">Convive bem com gatos.</param>
/// <param name="GoodWithKids">Convive bem com crianças.</param>
public sealed record PetReference(
    Guid Id,
    Guid? OwnerId,
    string Name,
    string Species,
    string? Size,
    bool IsActive,
    bool GoodWithDogs,
    bool GoodWithCats,
    bool GoodWithKids);
