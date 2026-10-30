using FluentValidation;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.DeleteMyOwner;

/// <summary>Exclui a conta de tutor logada a pedido do titular (LGPD, art. 18, VI).</summary>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
/// <param name="CurrentPassword">Reautenticação: a exclusão não tem volta.</param>
public sealed record DeleteMyOwnerCommand(Guid UserId, string? CurrentPassword) : ICommand<Unit>;

public sealed class DeleteMyOwnerCommandValidator : AbstractValidator<DeleteMyOwnerCommand>
{
    public DeleteMyOwnerCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithError(OwnersErrors.UserIdRequired);
        RuleFor(x => x.CurrentPassword).NotEmpty().WithError(OwnersErrors.DeletionPasswordRequired);
    }
}

/// <summary>
/// Exclui o tutor: cada módulo apaga ou anonimiza a sua parte (pets e fotos, conversas), o perfil
/// perde o CPF, a conta é anonimizada e as sessões caem na hora. Ficam as reservas, os pagamentos
/// (com o Customer do Stripe), as avaliações (o nome vira "Conta excluída") e a trilha. A conta de
/// anfitrião da mesma pessoa não é afetada.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Senha errada: <c>400</c> em <c>currentPassword</c>. Reserva em aberto: <c>422
/// OWNER_HAS_OPEN_BOOKING</c>, como na inativação.</item>
/// <item>A ordem deixa repetir: os outros módulos primeiro, depois o perfil e, por último, a conta.
/// Se algo falhar no meio, a pessoa ainda entra e pede de novo; cada passo é idempotente.</item>
/// <item>A foto de perfil sai do bucket só depois de a conta ter sido anonimizada.</item>
/// </list>
/// </remarks>
public sealed class DeleteMyOwnerCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnersUnitOfWork unitOfWork,
    IOwnerAccounts ownerAccounts,
    IOwnerBookings ownerBookings,
    IOwnerDataErasure dataErasure,
    IImageStorage imageStorage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<DeleteMyOwnerCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteMyOwnerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var owner = await ownerRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<Unit>.Failure(OwnersErrors.ProfileNotFound);

        if (!await ownerAccounts.VerifyPasswordAsync(command.UserId, command.CurrentPassword, cancellationToken).ConfigureAwait(false))
            return Result<Unit>.Failure(OwnersErrors.CurrentPasswordIncorrect);

        // Com reserva em aberto, a outra parte ficaria sem ninguém do lado do tutor.
        if (await ownerBookings.HasOpenBookingAsync(owner.Id.Value, cancellationToken).ConfigureAwait(false))
            return Result<Unit>.Failure(OwnersErrors.HasOpenBooking);

        var accounts = await ownerAccounts.GetByIdsAsync([command.UserId], cancellationToken).ConfigureAwait(false);
        var avatarUrl = accounts.Count > 0 ? accounts[0].AvatarUrl : null;

        await dataErasure.EraseAsync(command.UserId, owner.Id.Value, cancellationToken).ConfigureAwait(false);

        var wasErased = owner.IsErased;
        if (!wasErased)
        {
            owner.Erase(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var account = await ownerAccounts.EraseAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (account.IsFailure)
            return Result<Unit>.FromFailure(account);

        await imageStorage.DeleteAsync(avatarUrl, CancellationToken.None).ConfigureAwait(false);

        if (!wasErased)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.OwnerErased, AuditTargets.Owner, owner.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}
