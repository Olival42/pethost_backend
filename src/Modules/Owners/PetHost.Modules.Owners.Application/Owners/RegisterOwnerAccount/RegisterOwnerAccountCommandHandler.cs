using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;

/// <summary>
/// Cadastra o tutor num pedido só: confere o CPF, cria a conta no Auth e grava o
/// perfil de tutor apontando para ela.
/// </summary>
/// <remarks>
/// Conta e perfil ficam em módulos diferentes, então não há uma transação única. A
/// ordem minimiza o risco: tudo é validado antes (inclusive CPF e e-mail livres); a
/// conta é criada; e se gravar o perfil falhar, a conta é desfeita (compensação).
/// </remarks>
public sealed class RegisterOwnerAccountCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnersUnitOfWork unitOfWork,
    IOwnerAccounts ownerAccounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<RegisterOwnerAccountCommand, OwnerSessionResponse>
{
    public async Task<Result<OwnerSessionResponse>> HandleAsync(
        RegisterOwnerAccountCommand command,
        CancellationToken cancellationToken)
    {
        var cpf = Cpf.Create(command.Cpf);
        if (cpf.IsFailure)
            return Result<OwnerSessionResponse>.FromFailure(cpf);

        if (await ownerRepository.ExistsByCpfAsync(cpf.Value!, cancellationToken).ConfigureAwait(false))
            return Result<OwnerSessionResponse>.Failure(OwnersErrors.CpfAlreadyRegistered);

        var account = await ownerAccounts
            .CreateAsync(command.ToAccountRegistration(), cancellationToken)
            .ConfigureAwait(false);

        if (account.IsFailure)
            return Result<OwnerSessionResponse>.FromFailure(account);

        var session = account.Value!;
        var owner = Owner.Create(session.User.Id, cpf.Value!, timeProvider.GetUtcNow());

        try
        {
            ownerRepository.Add(owner);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Sem o perfil, a conta não pode ficar: seria um tutor sem CPF.
            // CancellationToken.None: a compensação roda mesmo se o request caiu.
            await ownerAccounts.DeleteAsync(session.User.Id, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await auditTrail
            .RecordAsync(
                new AuditRecord(AuditActions.OwnerRegistered, AuditTargets.Owner, owner.Id.Value, ActorId: session.User.Id),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<OwnerSessionResponse>.Success(new OwnerSessionResponse(
            session.AccessToken,
            session.RefreshToken,
            session.ExpiresAt,
            OwnerResponse.From(owner, session.User)));
    }
}
