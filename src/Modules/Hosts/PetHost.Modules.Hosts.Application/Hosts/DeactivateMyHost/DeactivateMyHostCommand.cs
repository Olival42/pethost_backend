using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.DeactivateMyHost;

/// <summary>A pessoa inativa a própria conta de anfitrião (e o perfil, se já aprovado).</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record DeactivateMyHostCommand(Guid UserId) : ICommand<Unit>;

public sealed class DeactivateMyHostCommandValidator : AbstractValidator<DeactivateMyHostCommand>
{
    public DeactivateMyHostCommandValidator() => RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
}

/// <summary>
/// Inativa perfil e conta (as sessões caem na hora). O perfil vai primeiro e volta a ativo se
/// a conta não puder ser inativada, para os dois nunca ficarem diferentes. Conta ainda sem
/// perfil (verificação em andamento) inativa só a conta.
/// </summary>
public sealed class DeactivateMyHostCommandHandler(
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<DeactivateMyHostCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeactivateMyHostCommand command, CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        var wasActive = host?.IsActive ?? false;

        if (host is not null && wasActive)
        {
            host.Deactivate(timeProvider.GetUtcNow());
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<Unit>.FromFailure(saved);
        }

        Result account;
        try
        {
            account = await accounts.DeactivateAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await UndoAsync(host, wasActive).ConfigureAwait(false);
            throw;
        }

        if (account.IsFailure)
        {
            await UndoAsync(host, wasActive).ConfigureAwait(false);
            return Result<Unit>.FromFailure(account);
        }

        if (host is not null && wasActive)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.HostDeactivated, AuditTargets.Host, host.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private async Task UndoAsync(Host? host, bool wasActive)
    {
        if (host is null || !wasActive)
            return;

        host.Reactivate(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
