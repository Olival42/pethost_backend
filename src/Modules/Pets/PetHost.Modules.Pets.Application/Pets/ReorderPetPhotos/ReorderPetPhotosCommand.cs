using FluentValidation;
using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.ReorderPetPhotos;

/// <summary>
/// Põe as fotos do pet na ordem enviada: a primeira vira a capa (vaga 1), a última fica na vaga n
/// (até 3). É também como se escolhe a capa: manda a foto escolhida primeiro.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="PetId">Vem da rota.</param>
/// <param name="PhotoIds">Todas as fotos do pet, cada uma uma vez, na ordem nova.</param>
public sealed record ReorderPetPhotosCommand(
    Guid UserId,
    string Role,
    Guid PetId,
    IReadOnlyList<Guid>? PhotoIds) : ICommand<PetResponse>;

/// <summary>
/// O formato da lista: veio, sem id vazio, sem repetição e com no máximo 3. Se ela tem
/// exatamente as fotos do pet, quem confere é o domínio.
/// </summary>
public sealed class ReorderPetPhotosCommandValidator : AbstractValidator<ReorderPetPhotosCommand>
{
    public ReorderPetPhotosCommandValidator()
    {
        RuleFor(x => x.PetId)
            .NotEmpty().WithMessage("Pet id is required.");

        RuleFor(x => x.PhotoIds)
            .NotNull().WithMessage(PetsErrors.PhotoOrderInvalid.Message)
            .Must(ids => ids is null || (ids.Count <= Pet.MaxPhotos && !ids.Contains(Guid.Empty) && ids.Distinct().Count() == ids.Count))
            .WithMessage(PetsErrors.PhotoOrderInvalid.Message);
    }
}

/// <summary>
/// Reordena as fotos. Só o dono (tutor ou anfitrião, pelo papel do token); pet de outra conta:
/// <c>404 PET_NOT_FOUND</c>. Id que não é foto do pet: <c>404 PET_PHOTO_NOT_FOUND</c>. A lista
/// precisa ter todas as fotos, cada uma uma vez: senão <c>400</c> em <c>photoIds</c>. Mesma ordem
/// de agora (já sem buraco) não grava nada.
/// </summary>
public sealed class ReorderPetPhotosCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IPetKeepers petKeepers,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReorderPetPhotosCommand, PetResponse>
{
    public async Task<Result<PetResponse>> HandleAsync(ReorderPetPhotosCommand command, CancellationToken cancellationToken)
    {
        var owned = await PetAccess
            .LoadOwnedAsync(petRepository, petKeepers, command.UserId, command.Role, command.PetId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<PetResponse>.FromFailure(owned);

        var pet = owned.Value!;
        var photoIds = (command.PhotoIds ?? []).Select(id => new PetPhotoId(id)).ToList();

        var reordered = pet.ReorderPhotos(photoIds, timeProvider.GetUtcNow());
        if (reordered.IsFailure)
            return Result<PetResponse>.FromFailure(reordered);

        if (reordered.Value)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await PetPhotoTrail.RecordAsync(auditTrail, pet, "photos.reordered", cancellationToken).ConfigureAwait(false);
        }

        return Result<PetResponse>.Success(await PetResponse.LoadAsync(pet, petKeepers, cancellationToken).ConfigureAwait(false));
    }
}
