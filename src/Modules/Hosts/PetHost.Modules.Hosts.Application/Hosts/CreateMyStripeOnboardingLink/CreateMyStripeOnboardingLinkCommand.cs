using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Application.Hosts.StripeOnboarding;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.CreateMyStripeOnboardingLink;

/// <summary>Igual ao embutido, mas pela página <b>hospedada</b> do Stripe: devolve um link de uso único.</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record CreateMyStripeOnboardingLinkCommand(Guid UserId) : ICommand<StripeOnboardingLinkResponse>;

public sealed class CreateMyStripeOnboardingLinkCommandValidator : AbstractValidator<CreateMyStripeOnboardingLinkCommand>
{
    public CreateMyStripeOnboardingLinkCommandValidator() => RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
}

/// <summary>Mesmas regras de <c>StartMyStripeOnboardingCommandHandler</c>, devolvendo o link hospedado.</summary>
public sealed class CreateMyStripeOnboardingLinkCommandHandler(HostPayoutSetup payoutSetup, IStripeConnect stripe)
    : ICommandHandler<CreateMyStripeOnboardingLinkCommand, StripeOnboardingLinkResponse>
{
    public async Task<Result<StripeOnboardingLinkResponse>> HandleAsync(
        CreateMyStripeOnboardingLinkCommand command,
        CancellationToken cancellationToken)
    {
        var host = await payoutSetup.LoadForOnboardingAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (host.IsFailure)
            return Result<StripeOnboardingLinkResponse>.FromFailure(host);

        var accountId = await payoutSetup.EnsureAccountAsync(host.Value!, cancellationToken).ConfigureAwait(false);
        if (accountId.IsFailure)
            return Result<StripeOnboardingLinkResponse>.FromFailure(accountId);

        var link = await stripe.CreateOnboardingLinkAsync(accountId.Value!, cancellationToken).ConfigureAwait(false);
        if (link.IsFailure)
            return Result<StripeOnboardingLinkResponse>.FromFailure(link);

        return Result<StripeOnboardingLinkResponse>.Success(new StripeOnboardingLinkResponse(
            link.Value!.Url,
            link.Value.ExpiresAt.ToUnixTimeSeconds(),
            StripeAccountResponse.From(host.Value!)));
    }
}
