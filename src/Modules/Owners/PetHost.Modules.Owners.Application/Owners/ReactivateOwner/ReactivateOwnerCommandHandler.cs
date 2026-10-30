using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.ReactivateOwner;

/// <summary>
/// Reativa a conta no Auth (que confere as credenciais e abre a sessão) e depois o perfil
/// de tutor. Para um tutor já ativo, funciona como um login.
/// </summary>
/// <remarks>
/// Se gravar o perfil falhar, a conta volta a ser inativada (compensação), para os dois
/// nunca ficarem com status diferentes.
/// </remarks>
public sealed class ReactivateOwnerCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnersUnitOfWork unitOfWork,
    IOwnerAccounts ownerAccounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReactivateOwnerCommand, OwnerSessionResponse>
{
    public async Task<Result<OwnerSessionResponse>> HandleAsync(
        ReactivateOwnerCommand command,
        CancellationToken cancellationToken)
    {
        var account = await ownerAccounts
            .ReactivateAsync(command.Email, command.Password, cancellationToken)
            .ConfigureAwait(false);

        if (account.IsFailure)
            return Result<OwnerSessionResponse>.FromFailure(account);

        var session = account.Value!;
        var owner = await ownerRepository.GetByUserIdAsync(session.User.Id, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<OwnerSessionResponse>.Failure(OwnersErrors.ProfileNotFound);

        if (!owner.IsActive)
        {
            try
            {
                owner.Reactivate(timeProvider.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await ownerAccounts.DeactivateAsync(session.User.Id, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            await auditTrail
                .RecordAsync(
                    new AuditRecord(AuditActions.OwnerReactivated, AuditTargets.Owner, owner.Id.Value, ActorId: session.User.Id),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<OwnerSessionResponse>.Success(new OwnerSessionResponse(
            session.AccessToken,
            session.RefreshToken,
            session.ExpiresAt,
            OwnerResponse.From(owner, session.User)));
    }
}
