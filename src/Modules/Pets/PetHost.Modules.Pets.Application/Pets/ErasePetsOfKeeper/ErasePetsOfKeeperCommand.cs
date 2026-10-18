using FluentValidation;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.ErasePetsOfKeeper;

/// <summary>
/// A conta do tutor ou do anfitrião foi excluída (LGPD): os pets dele são desativados e perdem as
/// fotos. Disparada sem usuário pelo contrato <c>IPersonalDataEraser</c>, antes de a conta ser
/// anonimizada.
/// </summary>
/// <param name="KeeperId">Id do tutor (<c>owner.owners.id</c>) ou do anfitrião (<c>host.hosts.id</c>).</param>
public sealed record ErasePetsOfKeeperCommand(KeeperType KeeperType, Guid KeeperId) : ICommand<int>;

public sealed class ErasePetsOfKeeperCommandValidator : AbstractValidator<ErasePetsOfKeeperCommand>
{
    public ErasePetsOfKeeperCommandValidator()
    {
        RuleFor(x => x.KeeperType).IsInEnum().WithMessage("Keeper type is invalid.");
        RuleFor(x => x.KeeperId).NotEmpty().WithMessage("Keeper id is required.");
    }
}

/// <summary>
/// A regra fica em <see cref="Pet.Erase"/>. Grava tudo de uma vez e só então apaga as fotos do
/// bucket: falha no bucket deixa um arquivo solto, nunca um pet apontando para foto apagada.
/// Devolve quantos pets foram tocados. Idempotente.
/// </summary>
public sealed class ErasePetsOfKeeperCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IImageStorage imageStorage,
    TimeProvider timeProvider) : ICommandHandler<ErasePetsOfKeeperCommand, int>
{
    public async Task<Result<int>> HandleAsync(ErasePetsOfKeeperCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var keeper = command.KeeperType == KeeperType.Owner
            ? PetKeeper.Owner(command.KeeperId)
            : PetKeeper.Host(command.KeeperId);

        var pets = await petRepository.ListByKeeperForUpdateAsync(keeper, cancellationToken).ConfigureAwait(false);
        if (pets.Count == 0)
            return Result<int>.Success(0);

        var now = timeProvider.GetUtcNow();
        var photos = pets.SelectMany(pet => pet.Erase(now)).ToList();

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var photo in photos)
            await imageStorage.DeleteAsync(photo, CancellationToken.None).ConfigureAwait(false);

        return Result<int>.Success(pets.Count);
    }
}
