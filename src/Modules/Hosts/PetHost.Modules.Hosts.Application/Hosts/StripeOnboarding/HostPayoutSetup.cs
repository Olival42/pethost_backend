using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.StripeOnboarding;

/// <summary>
/// Passos do cadastro de recebimento (Stripe Connect) repetidos entre os casos de uso: achar o
/// anfitrião que pode fazer o cadastro, criar a conta conectada na primeira vez e sincronizar o
/// espelho com o que o Stripe diz da conta.
/// </summary>
/// <remarks>
/// A ordem é <b>verificação (admin) → cadastro de recebimento (Stripe) → cantinho na busca</b>. A
/// primeira etapa está garantida pela existência do <see cref="Host"/>: ele só nasce quando o admin
/// aprova a solicitação.
/// </remarks>
public sealed class HostPayoutSetup(
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    IStripeConnect stripe,
    IHostPayoutsNotifier notifier,
    IHostBookings hostBookings,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
{
    /// <summary>
    /// O anfitrião da conta logada, se pode mexer no cadastro de recebimento. Conta ainda não
    /// aprovada pelo admin: <c>404 HOST_NOT_FOUND</c>; inativo ou suspenso:
    /// <c>422 HOST_STRIPE_ONBOARDING_NOT_ALLOWED</c>.
    /// </summary>
    public async Task<Result<Host>> LoadForOnboardingAsync(Guid userId, CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<Host>.Failure(HostsErrors.ProfileNotFound);

        var allowed = host.EnsureCanStartStripeOnboarding();

        return allowed.IsFailure ? Result<Host>.FromFailure(allowed) : Result<Host>.Success(host);
    }

    /// <summary>
    /// A conta conectada do anfitrião, criando-a na primeira vez com os dados conferidos na
    /// verificação (CPF; na empresa, CNPJ, razão social e endereço) e os da conta (nome,
    /// nascimento, telefone, endereço). Grava o <c>acct_...</c> e registra na trilha.
    /// </summary>
    public async Task<Result<string>> EnsureAccountAsync(Host host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (host.StripeAccountId is not null)
            return Result<string>.Success(host.StripeAccountId);

        var found = await accounts.GetByIdsAsync([host.UserId], cancellationToken).ConfigureAwait(false);
        var account = (found.Count > 0 ? found[0] : null)
            ?? throw new InvalidOperationException($"Host '{host.Id.Value}' points to an account that does not exist.");

        var company = host.Company;
        var draft = new StripeAccountDraft(
            host.Id.Value,
            host.UserId,
            host.PersonType,
            account.Email,
            account.FullName,
            account.BirthDate,
            account.Phone,
            account.Address,
            // Perfil sem CPF é de conta excluída, que nem chega aqui (inativo não abre cadastro).
            host.Cpf?.Value ?? throw new InvalidOperationException($"Host '{host.Id.Value}' has no CPF."),
            company?.Cnpj.Value,
            company?.LegalName,
            company?.TradeName,
            company is null
                ? null
                : new(
                    company.Address.ZipCode,
                    company.Address.Street,
                    company.Address.Number,
                    company.Address.Complement,
                    company.Address.Neighborhood,
                    company.Address.City,
                    company.Address.State),
            host.StripeAccountResets);

        var created = await stripe.CreateAccountAsync(draft, cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
            return created;

        var linked = host.LinkStripeAccount(created.Value!, timeProvider.GetUtcNow());
        if (linked.IsFailure)
            return Result<string>.FromFailure(linked);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<string>.FromFailure(saved);

        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.HostStripeAccountCreated, AuditTargets.Host, host.Id.Value), cancellationToken)
            .ConfigureAwait(false);

        return Result<string>.Success(created.Value!);
    }

    /// <summary>
    /// Relê a conta conectada no Stripe e grava o espelho. Quando os repasses são liberados, os
    /// cantinhos entram na busca: fica na trilha e o anfitrião recebe um e-mail. Quando são
    /// bloqueados, os cantinhos saem da busca (trilha) e, se o anfitrião tem reservas em aberto, o
    /// admin recebe a lista para decidir os reembolsos (os repasses ficam retidos no Payments).
    /// Quando o Stripe <b>recusa</b> a conta de vez (<c>rejected.*</c>), fica na trilha e avisa o
    /// anfitrião e o admin, uma vez só. Idempotente: pode rodar quantas vezes o webhook chegar.
    /// </summary>
    public async Task<Result> SyncAsync(Host host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (host.StripeAccountId is null)
            return Result.Failure(HostsErrors.StripeAccountNotFound);

        var snapshot = await stripe.GetAccountAsync(host.StripeAccountId, cancellationToken).ConfigureAwait(false);
        if (snapshot.IsFailure)
            return snapshot;

        var wasRejected = host.StripeOnboardingStatus == StripeOnboardingStatus.Rejected;
        var change = host.ApplyStripeSnapshot(snapshot.Value!, timeProvider.GetUtcNow());
        var rejectedNow = !wasRejected && host.StripeOnboardingStatus == StripeOnboardingStatus.Rejected;

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return saved;

        if (change == StripePayoutsChange.Enabled)
        {
            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.HostStripePayoutsEnabled, AuditTargets.Host, host.Id.Value), cancellationToken)
                .ConfigureAwait(false);

            var found = await accounts.GetByIdsAsync([host.UserId], cancellationToken).ConfigureAwait(false);
            if (found.Count > 0)
                await notifier.NotifyPayoutsEnabledAsync(new HostPayoutsNotice(found[0].Email, found[0].FullName), cancellationToken).ConfigureAwait(false);
        }
        else if (change == StripePayoutsChange.Disabled && !rejectedNow)
        {
            var details = new Dictionary<string, string?> { ["disabledReason"] = host.StripeDisabledReason };

            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.HostStripePayoutsDisabled, AuditTargets.Host, host.Id.Value, Details: details), cancellationToken)
                .ConfigureAwait(false);
        }

        if (rejectedNow)
            await NotifyRejectedAsync(host, cancellationToken).ConfigureAwait(false);

        if (change == StripePayoutsChange.Disabled)
            await NotifyBlockedWithBookingsAsync(host, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Os repasses foram bloqueados (ou a conta recusada) depois de liberados: se o anfitrião tem
    /// reservas em aberto, o admin recebe a lista. Os repasses ficam retidos no Payments; o admin
    /// decide se cancela (reembolsando o tutor) ou espera o Stripe liberar de novo.
    /// </summary>
    private async Task NotifyBlockedWithBookingsAsync(Host host, CancellationToken cancellationToken)
    {
        var open = await hostBookings.ListOpenBookingIdsAsync(host.Id.Value, cancellationToken).ConfigureAwait(false);
        if (open.Count == 0)
            return;

        var found = await accounts.GetByIdsAsync([host.UserId], cancellationToken).ConfigureAwait(false);
        var name = host.TradeName ?? (found.Count > 0 ? found[0].FullName : null) ?? host.Id.Value.ToString();

        await notifier
            .NotifyAdminPayoutsBlockedAsync(new HostPayoutsBlocked(host.Id.Value, name, host.StripeDisabledReason, open), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Recusa definitiva do Stripe: trilha, aviso ao anfitrião e alerta ao admin.</summary>
    private async Task NotifyRejectedAsync(Host host, CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, string?> { ["disabledReason"] = host.StripeDisabledReason };

        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.HostStripeAccountRejected, AuditTargets.Host, host.Id.Value, Details: details), cancellationToken)
            .ConfigureAwait(false);

        var found = await accounts.GetByIdsAsync([host.UserId], cancellationToken).ConfigureAwait(false);
        var account = found.Count > 0 ? found[0] : null;

        if (account is not null)
            await notifier.NotifyPayoutsRejectedAsync(new HostPayoutsNotice(account.Email, account.FullName), cancellationToken).ConfigureAwait(false);

        var name = host.TradeName ?? account?.FullName ?? host.Id.Value.ToString();
        await notifier
            .NotifyAdminPayoutsRejectedAsync(new HostPayoutsRejection(host.Id.Value, name, host.StripeDisabledReason), cancellationToken)
            .ConfigureAwait(false);
    }
}
