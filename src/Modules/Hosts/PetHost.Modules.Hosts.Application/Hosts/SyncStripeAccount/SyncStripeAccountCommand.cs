using FluentValidation;
using PetHost.Modules.Hosts.Application.Hosts.StripeOnboarding;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.SyncStripeAccount;

/// <summary>
/// Relê no Stripe a conta conectada <paramref name="StripeAccountId"/> e grava o espelho. Disparado
/// pelo webhook <c>account.updated</c>; conta que não é de nenhum anfitrião é ignorada.
/// </summary>
public sealed record SyncStripeAccountCommand(string StripeAccountId) : ICommand<Unit>;

public sealed class SyncStripeAccountCommandValidator : AbstractValidator<SyncStripeAccountCommand>
{
    public SyncStripeAccountCommandValidator() =>
        RuleFor(x => x.StripeAccountId).NotEmpty().WithError(HostsErrors.StripeAccountIdRequired);
}

/// <summary>
/// O webhook só avisa; o estado vem de uma leitura nova da conta no Stripe. Assim evento repetido
/// ou fora de ordem não estraga o espelho.
/// </summary>
public sealed class SyncStripeAccountCommandHandler(IHostRepository hostRepository, HostPayoutSetup payoutSetup)
    : ICommandHandler<SyncStripeAccountCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SyncStripeAccountCommand command, CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetByStripeAccountIdAsync(command.StripeAccountId.Trim(), cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<Unit>.Failure(HostsErrors.StripeAccountNotFound);

        var synced = await payoutSetup.SyncAsync(host, cancellationToken).ConfigureAwait(false);

        return synced.IsFailure ? Result<Unit>.FromFailure(synced) : Result<Unit>.Success(Unit.Value);
    }
}
