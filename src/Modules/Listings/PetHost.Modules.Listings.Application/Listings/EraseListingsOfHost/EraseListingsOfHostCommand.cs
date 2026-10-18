using FluentValidation;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.EraseListingsOfHost;

/// <summary>
/// A conta do anfitrião foi excluída (LGPD): os cantinhos dele saem do ar, sem fotos e sem o
/// endereço que leva à casa. Disparada sem usuário pelo contrato <c>IPersonalDataEraser</c>, antes
/// de a conta ser anonimizada.
/// </summary>
/// <param name="HostId">O anfitrião (<c>host.hosts.id</c>).</param>
public sealed record EraseListingsOfHostCommand(Guid HostId) : ICommand<int>;

public sealed class EraseListingsOfHostCommandValidator : AbstractValidator<EraseListingsOfHostCommand>
{
    public EraseListingsOfHostCommandValidator() => RuleFor(x => x.HostId).NotEmpty().WithMessage("Host id is required.");
}

/// <summary>
/// A regra fica em <see cref="Listing.Erase"/>. Grava tudo de uma vez e só então apaga as fotos do
/// bucket: falha no bucket deixa um arquivo solto, nunca um cantinho apontando para foto apagada.
/// Devolve quantos cantinhos foram tocados. Idempotente.
/// </summary>
public sealed class EraseListingsOfHostCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IImageStorage imageStorage,
    TimeProvider timeProvider) : ICommandHandler<EraseListingsOfHostCommand, int>
{
    public async Task<Result<int>> HandleAsync(EraseListingsOfHostCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var listings = await listingRepository.ListByHostForUpdateAsync(command.HostId, cancellationToken).ConfigureAwait(false);
        if (listings.Count == 0)
            return Result<int>.Success(0);

        var now = timeProvider.GetUtcNow();
        var photos = listings.SelectMany(listing => listing.Erase(now)).ToList();

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var photo in photos)
            await imageStorage.DeleteAsync(photo, CancellationToken.None).ConfigureAwait(false);

        return Result<int>.Success(listings.Count);
    }
}
