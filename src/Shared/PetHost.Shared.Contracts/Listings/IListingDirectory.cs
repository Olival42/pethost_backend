namespace PetHost.Shared.Contracts.Listings;

/// <summary>
/// Contrato público de leitura do módulo Listings (§5). Outro módulo que guarda o id do
/// cantinho — o Bookings, nas reservas — usa isto para ler as condições do cantinho
/// (capacidade, diária, o que aceita, dias em que recebe) sem referenciar o Listings nem fazer
/// JOIN entre schemas.
/// </summary>
public interface IListingDirectory
{
    /// <summary>O cantinho pelo id, ativo ou não, ou <c>null</c> se não existe.</summary>
    Task<ListingReference?> GetByIdAsync(Guid listingId, CancellationToken cancellationToken);

    /// <summary>Cantinhos pelos ids, numa consulta só. Id que não existe não aparece.</summary>
    Task<IReadOnlyList<ListingReference>> GetByIdsAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// O endereço exato de cada cantinho. Só para quem já tem direito a ele — o tutor de uma
    /// reserva confirmada (escopo, regra 7). Id que não existe não aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ListingAddressReference>> GetAddressesAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Os períodos que o anfitrião fechou na agenda do cantinho e que ainda não acabaram em
    /// <paramref name="from"/> (terminam nesse dia ou depois), do mais próximo para o mais distante.
    /// </summary>
    Task<IReadOnlyList<BlockedPeriod>> GetBlockedPeriodsAsync(
        Guid listingId,
        DateOnly from,
        CancellationToken cancellationToken);
}

/// <summary>Um período fechado na agenda do cantinho, de <paramref name="StartDate"/> a <paramref name="EndDate"/> (inclusive).</summary>
public sealed record BlockedPeriod(DateOnly StartDate, DateOnly EndDate);

/// <summary>Endereço exato do cantinho. CEP só com os 8 dígitos.</summary>
public sealed record ListingAddressReference(
    string ZipCode,
    string Street,
    string Number,
    string? Complement,
    string Neighborhood,
    string City,
    string State);

/// <summary>
/// As condições de um cantinho para reservar. Sem rua, número, CEP nem coordenada: o endereço
/// exato só é liberado depois do pagamento (escopo, regra 7).
/// </summary>
/// <param name="HostId">Anfitrião dono (<c>host.hosts.id</c>).</param>
/// <param name="Capacity">Quantos pets ao mesmo tempo, de tutores diferentes.</param>
/// <param name="NightlyRate">Diária por pet, em reais.</param>
/// <param name="AcceptedSpecies">Valores de <c>pets.species</c> aceitos (<c>dog</c>, <c>cat</c>...).</param>
/// <param name="AvailableWeekdays">Dias da semana em que o cantinho recebe pets.</param>
public sealed record ListingReference(
    Guid Id,
    Guid HostId,
    string Title,
    string Neighborhood,
    string City,
    string State,
    bool IsActive,
    int Capacity,
    decimal NightlyRate,
    IReadOnlyList<string> AcceptedSpecies,
    bool AcceptsSmall,
    bool AcceptsMedium,
    bool AcceptsLarge,
    IReadOnlyList<DayOfWeek> AvailableWeekdays);
