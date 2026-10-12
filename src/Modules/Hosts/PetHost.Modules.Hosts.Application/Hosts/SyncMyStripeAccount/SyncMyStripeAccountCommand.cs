using FluentValidation;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Application.Hosts.StripeOnboarding;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.SyncMyStripeAccount;

/// <summary>
/// Relê a conta no Stripe agora, sem esperar o webhook — o app chama ao voltar do cadastro.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record SyncMyStripeAccountCommand(Guid UserId) : ICommand<StripeAccountResponse>;

public sealed class SyncMyStripeAccountCommandValidator : AbstractValidator<SyncMyStripeAccountCommand>
{
    public SyncMyStripeAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
}

/// <summary>
/// Sem conta conectada ainda: <c>404 HOST_STRIPE_ACCOUNT_NOT_FOUND</c>. Não exige anfitrião ativo:
/// só lê a situação no Stripe.
/// </summary>
public sealed class SyncMyStripeAccountCommandHandler(IHostRepository hostRepository, HostPayoutSetup payoutSetup)
    : ICommandHandler<SyncMyStripeAccountCommand, StripeAccountResponse>
{
    public async Task<Result<StripeAccountResponse>> HandleAsync(SyncMyStripeAccountCommand command, CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<StripeAccountResponse>.Failure(HostsErrors.ProfileNotFound);

        var synced = await payoutSetup.SyncAsync(host, cancellationToken).ConfigureAwait(false);

        return synced.IsFailure
            ? Result<StripeAccountResponse>.FromFailure(synced)
            : Result<StripeAccountResponse>.Success(StripeAccountResponse.From(host));
    }
}
