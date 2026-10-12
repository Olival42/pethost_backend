using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.ResetHostStripeAccount;

/// <summary>
/// Admin desliga a conta conectada que o Stripe recusou, para o anfitrião abrir um cadastro de
/// recebimento novo (recusa por engano ou por dado digitado errado). Com motivo.
/// </summary>
/// <param name="AdminId">Vem do token do admin.</param>
public sealed record ResetHostStripeAccountCommand(Guid AdminId, Guid HostId, string? Reason) : ICommand<HostResponse>;

public sealed class ResetHostStripeAccountCommandValidator : AbstractValidator<ResetHostStripeAccountCommand>
{
    public ResetHostStripeAccountCommandValidator() => RuleFor(x => x.Reason).AdminReason();
}

/// <summary>
/// Só de conta recusada (<c>422 HOST_STRIPE_ACCOUNT_NOT_REJECTED</c>). A conta recusada fica no
/// histórico do anfitrião e na trilha; a próxima abertura do cadastro cria uma conta nova no Stripe.
/// </summary>
public sealed class ResetHostStripeAccountCommandHandler(
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ResetHostStripeAccountCommand, HostResponse>
{
    public async Task<Result<HostResponse>> HandleAsync(ResetHostStripeAccountCommand command, CancellationToken cancellationToken)
    {
        var id = new HostId(command.HostId);
        var host = await hostRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostResponse>.Failure(HostsErrors.NotFound(id));

        var reset = host.ResetStripeAccount(timeProvider.GetUtcNow());
        if (reset.IsFailure)
            return Result<HostResponse>.FromFailure(reset);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<HostResponse>.FromFailure(saved);

        var details = new Dictionary<string, string?> { ["rejectedStripeAccountId"] = host.StripeRejectedAccountId };
        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.HostStripeAccountReset, AuditTargets.Host, host.Id.Value, command.Reason?.Trim(), details), cancellationToken)
            .ConfigureAwait(false);

        return Result<HostResponse>.Success(await HostResponse.LoadAsync(host, accounts, cancellationToken).ConfigureAwait(false));
    }
}
