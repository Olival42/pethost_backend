using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.SuspendHost;

/// <summary>
/// Suspende perfil e conta (as sessões caem na hora). O perfil vai primeiro; se a conta não
/// puder ser suspensa, ele volta (compensação). Suspenso, as reservas dos cantinhos dele que
/// ainda não começaram (solicitada, aceita, confirmada) são canceladas pela plataforma, com 100%
/// de reembolso ao tutor; a estadia em andamento o admin encerra pelo check-out. Idempotente:
/// suspender de novo cancela o que tiver sobrado.
/// </summary>
public sealed class SuspendHostCommandHandler(
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    IHostBookings hostBookings,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<SuspendHostCommand, HostResponse>
{
    public async Task<Result<HostResponse>> HandleAsync(SuspendHostCommand command, CancellationToken cancellationToken)
    {
        var id = new HostId(command.HostId);
        var host = await hostRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostResponse>.Failure(HostsErrors.NotFound(id));

        var wasSuspended = host.IsSuspended;
        if (!wasSuspended)
        {
            host.Suspend(timeProvider.GetUtcNow());
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<HostResponse>.FromFailure(saved);
        }

        Result account;
        try
        {
            account = await accounts.SuspendAsync(host.UserId, command.Reason, command.AdminId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await UndoAsync(host, wasSuspended).ConfigureAwait(false);
            throw;
        }

        if (account.IsFailure)
        {
            await UndoAsync(host, wasSuspended).ConfigureAwait(false);
            return Result<HostResponse>.FromFailure(account);
        }

        if (!wasSuspended)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.HostSuspended, AuditTargets.Host, host.Id.Value, command.Reason), cancellationToken)
                .ConfigureAwait(false);
        }

        // Depois de gravar: anfitrião suspenso não entra mais nem para cancelar ou receber o pet.
        await hostBookings.CancelForSuspensionAsync(host.Id.Value, cancellationToken).ConfigureAwait(false);

        return Result<HostResponse>.Success(await HostResponse.LoadAsync(host, accounts, cancellationToken).ConfigureAwait(false));
    }

    private async Task UndoAsync(Host host, bool wasSuspended)
    {
        if (wasSuspended)
            return;

        host.LiftSuspension(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

/// <summary>
/// Tira a suspensão do perfil e da conta. Recusa se o CPF/CNPJ foi liberado. O perfil vai
/// primeiro (é ele que tem a regra); se a conta falhar, ele volta a ficar suspenso.
/// </summary>
public sealed class LiftHostSuspensionCommandHandler(
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<LiftHostSuspensionCommand, HostResponse>
{
    public async Task<Result<HostResponse>> HandleAsync(LiftHostSuspensionCommand command, CancellationToken cancellationToken)
    {
        var id = new HostId(command.HostId);
        var host = await hostRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostResponse>.Failure(HostsErrors.NotFound(id));

        var wasSuspended = host.IsSuspended;
        var lifted = host.LiftSuspension(timeProvider.GetUtcNow());
        if (lifted.IsFailure)
            return Result<HostResponse>.FromFailure(lifted);

        if (wasSuspended)
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<HostResponse>.FromFailure(saved);
        }

        Result account;
        try
        {
            account = await accounts.LiftSuspensionAsync(host.UserId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await UndoAsync(host, wasSuspended).ConfigureAwait(false);
            throw;
        }

        if (account.IsFailure)
        {
            await UndoAsync(host, wasSuspended).ConfigureAwait(false);
            return Result<HostResponse>.FromFailure(account);
        }

        if (wasSuspended)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.HostSuspensionLifted, AuditTargets.Host, host.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostResponse>.Success(await HostResponse.LoadAsync(host, accounts, cancellationToken).ConfigureAwait(false));
    }

    private async Task UndoAsync(Host host, bool wasSuspended)
    {
        if (!wasSuspended)
            return;

        host.Suspend(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

/// <summary>
/// Libera o CPF/CNPJ de um anfitrião suspenso: os valores ficam no perfil como evidência, mas
/// deixam de contar nas regras de unicidade, e o dono de verdade pode se cadastrar. A trilha
/// guarda o CPF mascarado (e o CNPJ, dado público da empresa).
/// </summary>
public sealed class ReleaseHostIdentityCommandHandler(
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReleaseHostIdentityCommand, HostResponse>
{
    public async Task<Result<HostResponse>> HandleAsync(ReleaseHostIdentityCommand command, CancellationToken cancellationToken)
    {
        var id = new HostId(command.HostId);
        var host = await hostRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostResponse>.Failure(HostsErrors.NotFound(id));

        var alreadyReleased = host.IsIdentityReleased;
        var released = host.ReleaseIdentity(timeProvider.GetUtcNow());
        if (released.IsFailure)
            return Result<HostResponse>.FromFailure(released);

        if (!alreadyReleased)
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<HostResponse>.FromFailure(saved);

            var details = new Dictionary<string, string?> { ["cpf"] = host.Cpf?.ToString() };
            if (host.Cnpj is not null)
                details["cnpj"] = host.Cnpj.Value;

            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.HostIdentityReleased, AuditTargets.Host, host.Id.Value, command.Reason, details), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostResponse>.Success(await HostResponse.LoadAsync(host, accounts, cancellationToken).ConfigureAwait(false));
    }
}
