using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Application.Hosts.StripeOnboarding;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.StartMyStripeOnboarding;

/// <summary>
/// Abre (ou continua) o cadastro de recebimento com o componente <b>embutido</b> do Stripe: cria a
/// conta conectada na primeira vez e devolve a sessão para o app.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record StartMyStripeOnboardingCommand(Guid UserId) : ICommand<StripeOnboardingSessionResponse>;

public sealed class StartMyStripeOnboardingCommandValidator : AbstractValidator<StartMyStripeOnboardingCommand>
{
    public StartMyStripeOnboardingCommandValidator() => RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
}

/// <summary>
/// Só anfitrião aprovado pelo admin (senão <c>404 HOST_NOT_FOUND</c>), ativo e sem suspensão. A
/// conta conectada é criada uma vez; as próximas chamadas só abrem uma sessão nova.
/// </summary>
public sealed class StartMyStripeOnboardingCommandHandler(HostPayoutSetup payoutSetup, IStripeConnect stripe)
    : ICommandHandler<StartMyStripeOnboardingCommand, StripeOnboardingSessionResponse>
{
    public async Task<Result<StripeOnboardingSessionResponse>> HandleAsync(
        StartMyStripeOnboardingCommand command,
        CancellationToken cancellationToken)
    {
        var host = await payoutSetup.LoadForOnboardingAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (host.IsFailure)
            return Result<StripeOnboardingSessionResponse>.FromFailure(host);

        var accountId = await payoutSetup.EnsureAccountAsync(host.Value!, cancellationToken).ConfigureAwait(false);
        if (accountId.IsFailure)
            return Result<StripeOnboardingSessionResponse>.FromFailure(accountId);

        var session = await stripe.CreateOnboardingSessionAsync(accountId.Value!, cancellationToken).ConfigureAwait(false);
        if (session.IsFailure)
            return Result<StripeOnboardingSessionResponse>.FromFailure(session);

        return Result<StripeOnboardingSessionResponse>.Success(new StripeOnboardingSessionResponse(
            session.Value!.ClientSecret,
            session.Value.PublishableKey,
            session.Value.ExpiresAt.ToUnixTimeSeconds(),
            StripeAccountResponse.From(host.Value!)));
    }
}
