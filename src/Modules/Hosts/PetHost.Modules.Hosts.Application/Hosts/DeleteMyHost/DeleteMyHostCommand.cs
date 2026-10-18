using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.DeleteMyHost;

/// <summary>Exclui a conta de anfitrião logada a pedido do titular (LGPD, art. 18, VI).</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="CurrentPassword">Reautenticação: a exclusão não tem volta.</param>
public sealed record DeleteMyHostCommand(Guid UserId, string? CurrentPassword) : ICommand<Unit>;

public sealed class DeleteMyHostCommandValidator : AbstractValidator<DeleteMyHostCommand>
{
    public DeleteMyHostCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage(HostsErrors.DeletionPasswordRequired.Message);
    }
}

/// <summary>
/// Exclui o anfitrião: cada módulo apaga ou anonimiza a sua parte (pets e fotos, cantinhos sem fotos
/// nem endereço exato, conversas), as solicitações de verificação somem com os documentos do bucket
/// privado, o perfil perde o CPF, a conta é anonimizada e as sessões caem na hora. Ficam as reservas,
/// os pagamentos e repasses (com a conta conectada do Stripe), as avaliações (o nome vira "Conta
/// excluída"), os dados da empresa e a trilha. Conta ainda sem perfil (verificação em andamento)
/// também pode ser excluída. A conta de tutor da mesma pessoa não é afetada.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Senha errada: <c>400</c> em <c>currentPassword</c>. Reserva em aberto: <c>422
/// HOST_HAS_OPEN_BOOKING</c>, como na inativação.</item>
/// <item>A ordem deixa repetir: os outros módulos primeiro, depois as solicitações e o perfil e,
/// por último, a conta. Se algo falhar no meio, a pessoa ainda entra e pede de novo.</item>
/// <item>Arquivos (documentos e foto de perfil) saem do bucket só depois de o banco gravar.</item>
/// </list>
/// </remarks>
public sealed class DeleteMyHostCommandHandler(
    IHostRepository hostRepository,
    IHostApplicationRepository applicationRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    IHostBookings hostBookings,
    IHostDataErasure dataErasure,
    IImageStorage imageStorage,
    IPrivateImageStorage privateImageStorage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<DeleteMyHostCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteMyHostCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await accounts.VerifyPasswordAsync(command.UserId, command.CurrentPassword, cancellationToken).ConfigureAwait(false))
            return Result<Unit>.Failure(HostsErrors.CurrentPasswordIncorrect);

        var host = await hostRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);

        // Com reserva em aberto, o pedido ficaria sem resposta e a estadia sem check-in nem check-out.
        if (host is not null && await hostBookings.HasOpenBookingAsync(host.Id.Value, cancellationToken).ConfigureAwait(false))
            return Result<Unit>.Failure(HostsErrors.HasOpenBooking);

        var found = await accounts.GetByIdsAsync([command.UserId], cancellationToken).ConfigureAwait(false);
        var avatarUrl = found.Count > 0 ? found[0].AvatarUrl : null;

        await dataErasure.EraseAsync(command.UserId, host?.Id.Value, cancellationToken).ConfigureAwait(false);

        var wasErased = host?.IsErased ?? true;

        var documents = await EraseProfileAsync(command.UserId, host, cancellationToken).ConfigureAwait(false);
        if (documents.IsFailure)
            return Result<Unit>.FromFailure(documents);

        var account = await accounts.EraseAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (account.IsFailure)
            return Result<Unit>.FromFailure(account);

        foreach (var key in documents.Value!)
            await privateImageStorage.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);

        await imageStorage.DeleteAsync(avatarUrl, CancellationToken.None).ConfigureAwait(false);

        if (host is not null && !wasErased)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.HostErased, AuditTargets.Host, host.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>
    /// Apaga as solicitações de verificação (com os documentos) e tira o CPF do perfil, numa
    /// gravação só. Devolve as chaves dos documentos, para apagar do bucket privado depois.
    /// </summary>
    private async Task<Result<IReadOnlyList<string>>> EraseProfileAsync(Guid userId, Host? host, CancellationToken cancellationToken)
    {
        var applications = await applicationRepository.ListByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var keys = applications.SelectMany(a => a.Documents).Select(d => d.StorageKey).ToList();

        foreach (var application in applications)
            applicationRepository.Remove(application);

        var changed = applications.Count > 0 || host is { IsErased: false };
        host?.Erase(timeProvider.GetUtcNow());

        if (changed)
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<IReadOnlyList<string>>.FromFailure(saved);
        }

        return Result<IReadOnlyList<string>>.Success(keys);
    }
}
