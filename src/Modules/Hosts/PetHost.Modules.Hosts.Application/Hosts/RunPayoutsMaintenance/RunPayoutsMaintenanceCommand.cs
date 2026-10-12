using System.Globalization;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.StripeOnboarding;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.RunPayoutsMaintenance;

/// <summary>
/// Uma rodada da manutenção do cadastro de recebimento (tarefa em segundo plano): relê no Stripe
/// as contas que ainda não recebem — e, de tempos em tempos, as que recebem —, para o PetHost não
/// depender só do webhook; depois lembra por e-mail quem ainda não terminou o cadastro.
/// </summary>
public sealed record RunPayoutsMaintenanceCommand : ICommand<RunPayoutsMaintenanceResponse>;

/// <summary>O que a rodada fez.</summary>
/// <param name="Synced">Contas relidas no Stripe com sucesso.</param>
/// <param name="Failed">Contas que não deu para reler (tentam de novo na próxima rodada).</param>
/// <param name="Reminded">Lembretes enviados.</param>
public sealed record RunPayoutsMaintenanceResponse(int Synced, int Failed, int Reminded);

/// <remarks>
/// Se o Stripe estiver fora do ar (ou sem chave), a releitura para na primeira falha desse tipo:
/// não adianta bater nele para cada anfitrião. Os lembretes saem mesmo assim. Conta recusada de
/// vez (<c>rejected.*</c>) não é relida nem lembrada: é com o admin.
/// </remarks>
public sealed class RunPayoutsMaintenanceCommandHandler(
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    HostPayoutSetup payoutSetup,
    IHostPayoutsNotifier notifier,
    IHostPayoutsPolicy policy,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<RunPayoutsMaintenanceCommand, RunPayoutsMaintenanceResponse>
{
    public async Task<Result<RunPayoutsMaintenanceResponse>> HandleAsync(
        RunPayoutsMaintenanceCommand command,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var hosts = await hostRepository
            .ListForPayoutsMaintenanceAsync(now - policy.EnabledResyncInterval, cancellationToken)
            .ConfigureAwait(false);

        var (synced, failed) = await SyncAsync(hosts, cancellationToken).ConfigureAwait(false);
        var reminded = await RemindAsync(hosts, cancellationToken).ConfigureAwait(false);

        return Result<RunPayoutsMaintenanceResponse>.Success(new RunPayoutsMaintenanceResponse(synced, failed, reminded));
    }

    private async Task<(int Synced, int Failed)> SyncAsync(IReadOnlyList<Host> hosts, CancellationToken cancellationToken)
    {
        int synced = 0, failed = 0;

        foreach (var host in hosts.Where(h => h.StripeAccountId is not null && h.StripeOnboardingStatus != StripeOnboardingStatus.Rejected))
        {
            var result = await payoutSetup.SyncAsync(host, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                synced++;
                continue;
            }

            failed++;
            if (result.FirstError == HostsErrors.StripeUnavailable)
                break;
        }

        return (synced, failed);
    }

    private async Task<int> RemindAsync(IReadOnlyList<Host> hosts, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = hosts.Where(h => h.IsStripeReminderDue(now, policy.ReminderSchedule)).ToList();
        if (due.Count == 0)
            return 0;

        // Uma consulta ao Auth para todos os lembretes da rodada.
        var found = await accounts.GetByIdsAsync([.. due.Select(h => h.UserId)], cancellationToken).ConfigureAwait(false);
        var byId = found.ToDictionary(a => a.Id);
        var reminded = 0;

        foreach (var host in due)
        {
            if (!byId.TryGetValue(host.UserId, out var account))
                continue;

            // Grava antes de mandar: se gravar falhar, a pessoa não recebe o mesmo lembrete duas vezes.
            host.RecordStripeReminder(now);
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                continue;

            var status = StripeOnboardingStatusValues.ToWire(host.StripeOnboardingStatus);
            await notifier
                .NotifyPayoutSetupReminderAsync(new HostPayoutsReminder(account.Email, account.FullName, status, host.StripeRemindersSent), cancellationToken)
                .ConfigureAwait(false);

            var details = new Dictionary<string, string?>
            {
                ["status"] = status,
                ["reminder"] = host.StripeRemindersSent.ToString(CultureInfo.InvariantCulture),
            };
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.HostStripeReminderSent, AuditTargets.Host, host.Id.Value, Details: details), cancellationToken)
                .ConfigureAwait(false);

            reminded++;
        }

        return reminded;
    }
}
