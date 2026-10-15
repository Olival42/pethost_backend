using FluentValidation;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.AvailabilityBlocks;

/// <summary>O anfitrião reabre as datas de um bloqueio. Devolve a agenda como ficou.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="ListingId">Vem da rota.</param>
/// <param name="BlockId">Vem da rota.</param>
public sealed record UnblockListingDatesCommand(Guid UserId, Guid ListingId, Guid BlockId)
    : ICommand<IReadOnlyList<AvailabilityBlockResponse>>;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class UnblockListingDatesCommandValidator : AbstractValidator<UnblockListingDatesCommand>
{
    public UnblockListingDatesCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage("Listing id is required.");

        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block id is required.");
    }
}

/// <summary>
/// Só o dono do cantinho, ativo e sem suspensão. Bloqueio que não é do cantinho:
/// <c>404 LISTING_AVAILABILITY_BLOCK_NOT_FOUND</c>. O bloqueio sai da tabela (a trilha guarda o período).
/// </summary>
public sealed class UnblockListingDatesCommandHandler(
    IListingRepository listingRepository,
    IAvailabilityBlockRepository blockRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UnblockListingDatesCommand, IReadOnlyList<AvailabilityBlockResponse>>
{
    public async Task<Result<IReadOnlyList<AvailabilityBlockResponse>>> HandleAsync(UnblockListingDatesCommand command, CancellationToken cancellationToken)
    {
        var host = await ListingAccess.ResolveHostAsync(listingHosts, command.UserId, requireAvailable: true, cancellationToken).ConfigureAwait(false);
        if (host.IsFailure)
            return Result<IReadOnlyList<AvailabilityBlockResponse>>.FromFailure(host);

        var owned = await ListingAccess
            .LoadOwnedAsync(listingRepository, listingHosts, command.UserId, command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<IReadOnlyList<AvailabilityBlockResponse>>.FromFailure(owned);

        var listingId = owned.Value!.Listing.Id;
        var blockId = new AvailabilityBlockId(command.BlockId);

        var block = await blockRepository.GetAsync(listingId, blockId, cancellationToken).ConfigureAwait(false);
        if (block is null)
            return Result<IReadOnlyList<AvailabilityBlockResponse>>.Failure(ListingsErrors.AvailabilityBlockNotFound(blockId));

        blockRepository.Remove(block);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AvailabilityTrail.RecordAsync(auditTrail, AuditActions.ListingDatesUnblocked, block, cancellationToken).ConfigureAwait(false);

        var open = await blockRepository.ListOpenAsync(listingId, StayWindow.Today(timeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<AvailabilityBlockResponse>>.Success([.. open.Select(b => AvailabilityBlockResponse.From(b, withReason: true))]);
    }
}
