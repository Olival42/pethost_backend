using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.CheckPetCompatibility;

/// <summary>"Combina com seu pet": cruza um pet do tutor logado com o cantinho e, com as datas, com os outros hóspedes.</summary>
/// <param name="UserId">Vem do access token (conta de tutor).</param>
/// <param name="PetId">Pet do tutor logado. Obrigatório.</param>
/// <param name="CheckIn">Chegada, <c>yyyy-MM-dd</c>, junto com <paramref name="CheckOut"/>. Sem as datas, só a casa entra na conta.</param>
public sealed record CheckPetCompatibilityQuery(
    Guid UserId,
    Guid ListingId,
    Guid? PetId,
    string? CheckIn = null,
    string? CheckOut = null) : IQuery<PetCompatibilityResponse>;

/// <summary>
/// Lê o cantinho, o pet e (com datas) os pets das estadias aceitas, confirmadas e em andamento
/// do período, e aplica <see cref="PetCompatibility"/>. Os hóspedes do próprio tutor não entram:
/// ele já conhece os pets dele.
/// </summary>
/// <remarks>
/// Mesma visibilidade do detalhe para quem não é dono: cantinho inativo, de anfitrião inativo,
/// suspenso ou sem repasses liberados é <c>404 LISTING_NOT_FOUND</c>. Pet inexistente, inativo
/// ou de outro tutor: <c>404 LISTING_PET_NOT_FOUND</c>. <c>petId</c> ausente e datas inválidas
/// (mesmas regras da busca): <c>400</c>, todos os campos juntos.
/// </remarks>
public sealed class CheckPetCompatibilityQueryHandler(
    IListingRepository listingRepository,
    IListingHosts listingHosts,
    IListingPets listingPets,
    IListingBookings listingBookings,
    TimeProvider timeProvider) : IQueryHandler<CheckPetCompatibilityQuery, PetCompatibilityResponse>
{
    public async Task<Result<PetCompatibilityResponse>> HandleAsync(
        CheckPetCompatibilityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new List<Error>();
        if (query.PetId is null)
            errors.Add(ListingsErrors.CompatibilityPetRequired);

        var window = StayWindow.Parse(query.CheckIn, query.CheckOut, StayWindow.Today(timeProvider.GetUtcNow()));
        if (window.IsFailure)
            errors.AddRange(window.Errors!);

        if (errors.Count > 0)
            return Result<PetCompatibilityResponse>.Failure(errors);

        var petId = query.PetId.GetValueOrDefault();
        var id = new ListingId(query.ListingId);

        var listing = await listingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (listing is null || !listing.IsActive)
            return Result<PetCompatibilityResponse>.Failure(ListingsErrors.NotFound(id));

        var hosts = await listingHosts.GetSummariesAsync([listing.HostId], cancellationToken).ConfigureAwait(false);
        if (hosts.GetValueOrDefault(listing.HostId) is not { IsAvailable: true, PayoutsEnabled: true })
            return Result<PetCompatibilityResponse>.Failure(ListingsErrors.NotFound(id));

        var ownerId = await listingPets.FindOwnerIdAsync(query.UserId, cancellationToken).ConfigureAwait(false);
        if (ownerId is null)
            return Result<PetCompatibilityResponse>.Failure(ListingsErrors.OwnerNotFound);

        var pets = await listingPets.GetByIdsAsync([petId], cancellationToken).ConfigureAwait(false);
        var pet = pets.FirstOrDefault(p => p.Profile.Id == petId);
        if (pet is null || !pet.IsActive || pet.OwnerId != ownerId)
            return Result<PetCompatibilityResponse>.Failure(ListingsErrors.PetNotFound(petId));

        var guests = window.Value is { } stay
            ? await GuestsAsync(listing, stay, ownerId.Value, cancellationToken).ConfigureAwait(false)
            : null;

        var warnings = PetCompatibility.Check(listing, pet.Profile, guests ?? []);

        return Result<PetCompatibilityResponse>.Success(PetCompatibilityResponse.From(listing, pet.Profile, warnings, guests));
    }

    /// <summary>Os pets de outros tutores hospedados no período, na ordem de chegada.</summary>
    private async Task<IReadOnlyList<CompatibilityPet>> GuestsAsync(
        Listing listing,
        StayWindow stay,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var stays = await listingBookings
            .GetGuestsAsync(listing.Id.Value, stay.CheckIn, stay.CheckOut, cancellationToken)
            .ConfigureAwait(false);

        var guestIds = stays.Where(s => s.OwnerId != ownerId).Select(s => s.PetId).Distinct().ToList();
        if (guestIds.Count == 0)
            return [];

        var pets = await listingPets.GetByIdsAsync(guestIds, cancellationToken).ConfigureAwait(false);
        var byId = pets.ToDictionary(p => p.Profile.Id, p => p.Profile);

        return [.. guestIds.Where(byId.ContainsKey).Select(guestId => byId[guestId])];
    }
}
