using FluentValidation;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.AvailabilityBlocks;

/// <summary>O anfitrião fecha datas na agenda do cantinho: ninguém pede estadia que passe por elas.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="ListingId">Vem da rota.</param>
/// <param name="StartDate">Primeiro dia bloqueado, <c>yyyy-MM-dd</c> (a partir de hoje, horário de Brasília).</param>
/// <param name="EndDate">Último dia bloqueado, inclusive (pode ser o mesmo dia do início).</param>
/// <param name="Reason">Opcional, até 120 caracteres, só para o anfitrião.</param>
public sealed record BlockListingDatesCommand(
    Guid UserId,
    Guid ListingId,
    string? StartDate,
    string? EndDate,
    string? Reason) : ICommand<AvailabilityBlockResponse>;

/// <summary>
/// Formato e regras das datas e do motivo, todos os erros juntos num só 400. As datas são
/// conferidas de novo no domínio (o dia pode ter virado).
/// </summary>
public sealed class BlockListingDatesCommandValidator : AbstractValidator<BlockListingDatesCommand>
{
    public BlockListingDatesCommandValidator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage("Listing id is required.");

        RuleFor(x => x.StartDate)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(ListingsErrors.AvailabilityBlockStartRequired.Message)
            .Must(text => AvailabilityDates.TryParse(text, out _)).WithMessage(ListingsErrors.AvailabilityBlockStartInvalid.Message);

        RuleFor(x => x.EndDate)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(ListingsErrors.AvailabilityBlockEndRequired.Message)
            .Must(text => AvailabilityDates.TryParse(text, out _)).WithMessage(ListingsErrors.AvailabilityBlockEndInvalid.Message);

        RuleFor(x => x.Reason)
            .Must(reason => reason is null || reason.Trim().Length <= AvailabilityBlock.ReasonMaxLength)
            .WithMessage(ListingsErrors.AvailabilityBlockReasonTooLong.Message);

        RuleFor(x => x).Custom((command, context) =>
        {
            if (!AvailabilityDates.TryParse(command.StartDate, out var start))
                return;

            // Fim ilegível: o início ainda é conferido sozinho, para os erros dos dois campos voltarem juntos.
            var hasEnd = AvailabilityDates.TryParse(command.EndDate, out var end);

            var period = AvailabilityBlock.CheckPeriod(start, hasEnd ? end : start, StayWindow.Today(timeProvider.GetUtcNow()));
            if (period.IsFailure)
            {
                foreach (var error in period.Errors!.Where(error => hasEnd || error.Field == ListingsErrors.AvailabilityBlockStartInThePast.Field))
                    context.AddFailure(error.Field ?? string.Empty, error.Message);
            }
        });
    }
}

/// <summary>
/// Só o dono do cantinho, ativo e sem suspensão (<c>403 LISTING_HOST_DEACTIVATED</c>); cantinho de
/// outro anfitrião: <c>404 LISTING_NOT_FOUND</c>. Vale também para cantinho inativo. As regras
/// ficam em <see cref="AvailabilityBlock.Create"/>: sobrepor outro bloqueio dá
/// <c>422 LISTING_AVAILABILITY_BLOCK_OVERLAP</c>; passar do limite,
/// <c>422 LISTING_AVAILABILITY_BLOCK_LIMIT_REACHED</c>.
/// </summary>
/// <remarks>
/// Reservas já pedidas, aceitas ou confirmadas nessas datas continuam: o bloqueio vale para os
/// pedidos novos (e para a busca por datas). O anfitrião recusa ou cancela, se for o caso.
/// </remarks>
public sealed class BlockListingDatesCommandHandler(
    IListingRepository listingRepository,
    IAvailabilityBlockRepository blockRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<BlockListingDatesCommand, AvailabilityBlockResponse>
{
    public async Task<Result<AvailabilityBlockResponse>> HandleAsync(BlockListingDatesCommand command, CancellationToken cancellationToken)
    {
        var host = await ListingAccess.ResolveHostAsync(listingHosts, command.UserId, requireAvailable: true, cancellationToken).ConfigureAwait(false);
        if (host.IsFailure)
            return Result<AvailabilityBlockResponse>.FromFailure(host);

        var owned = await ListingAccess
            .LoadOwnedAsync(listingRepository, listingHosts, command.UserId, command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<AvailabilityBlockResponse>.FromFailure(owned);

        _ = AvailabilityDates.TryParse(command.StartDate, out var start);
        _ = AvailabilityDates.TryParse(command.EndDate, out var end);

        var now = timeProvider.GetUtcNow();
        var listingId = owned.Value!.Listing.Id;
        var open = await blockRepository.ListOpenAsync(listingId, StayWindow.Today(now), cancellationToken).ConfigureAwait(false);

        var block = AvailabilityBlock.Create(listingId, start, end, command.Reason, open, now);
        if (block.IsFailure)
            return Result<AvailabilityBlockResponse>.FromFailure(block);

        blockRepository.Add(block.Value!);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AvailabilityTrail.RecordAsync(auditTrail, AuditActions.ListingDatesBlocked, block.Value!, cancellationToken).ConfigureAwait(false);

        return Result<AvailabilityBlockResponse>.Success(AvailabilityBlockResponse.From(block.Value!, withReason: true));
    }
}
