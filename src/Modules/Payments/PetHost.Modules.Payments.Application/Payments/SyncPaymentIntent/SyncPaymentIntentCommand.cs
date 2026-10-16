using FluentValidation;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments.SyncPaymentIntent;

/// <summary>
/// Um webhook do Stripe avisou que uma cobrança mudou (paga, falhou, reembolsada, contestada). O
/// aviso é só um aviso: a cobrança é relida na API e o pagamento, reconciliado. Disparada sem usuário.
/// </summary>
/// <param name="StripeObjectId">
/// O objeto do evento: a cobrança (<c>pi_...</c>), a cobrança efetiva (<c>ch_...</c>), o reembolso
/// (<c>re_...</c>) ou a contestação (<c>dp_...</c>).
/// </param>
public sealed record SyncPaymentIntentCommand(string StripeObjectId) : ICommand<SyncPaymentIntentResponse>;

/// <summary>O que o webhook achou.</summary>
/// <param name="Matched">A cobrança é de uma parcela do PetHost (senão, o evento é ignorado).</param>
public sealed record SyncPaymentIntentResponse(bool Matched);

public sealed class SyncPaymentIntentCommandValidator : AbstractValidator<SyncPaymentIntentCommand>
{
    public SyncPaymentIntentCommandValidator()
    {
        RuleFor(x => x.StripeObjectId)
            .NotEmpty().WithMessage("Stripe object id is required.");
    }
}

/// <summary>
/// Idempotente e imune a evento fora de ordem: o que vale é o estado relido. Stripe fora do ar ou
/// pagamento alterado ao mesmo tempo: falha — o endpoint responde 500 e o Stripe reenvia depois.
/// </summary>
public sealed class SyncPaymentIntentCommandHandler(
    IPaymentRepository paymentRepository,
    IStripePayments stripe,
    PaymentReconciler reconciler,
    PaymentCommitter committer) : ICommandHandler<SyncPaymentIntentCommand, SyncPaymentIntentResponse>
{
    /// <summary>Prefixo dos ids de cobrança (<c>PaymentIntent</c>) no Stripe.</summary>
    public const string PaymentIntentPrefix = "pi_";

    public async Task<Result<SyncPaymentIntentResponse>> HandleAsync(SyncPaymentIntentCommand command, CancellationToken cancellationToken)
    {
        var intentId = command.StripeObjectId;

        if (!intentId.StartsWith(PaymentIntentPrefix, StringComparison.Ordinal))
        {
            var resolved = await stripe.ResolveIntentIdAsync(command.StripeObjectId, cancellationToken).ConfigureAwait(false);
            if (resolved.IsFailure)
                return Result<SyncPaymentIntentResponse>.FromFailure(resolved);

            if (resolved.Value is not { } found)
                return Result<SyncPaymentIntentResponse>.Success(new SyncPaymentIntentResponse(false));

            intentId = found;
        }

        var payment = await paymentRepository.GetByIntentAsync(intentId, cancellationToken).ConfigureAwait(false);
        if (payment?.FindByIntent(intentId) is not { } installment)
            return Result<SyncPaymentIntentResponse>.Success(new SyncPaymentIntentResponse(false));

        var effects = new PaymentEffects();

        var refreshed = await reconciler.RefreshAsync(payment, installment, effects, cancellationToken).ConfigureAwait(false);
        if (refreshed.IsFailure)
            return Result<SyncPaymentIntentResponse>.FromFailure(refreshed);

        var reconciled = await reconciler.ReconcileAsync(payment, effects, cancellationToken).ConfigureAwait(false);
        if (reconciled.IsFailure)
            return Result<SyncPaymentIntentResponse>.FromFailure(reconciled);

        var committed = await committer.CommitAsync(payment, effects, cancellationToken).ConfigureAwait(false);
        if (committed.IsFailure)
            return Result<SyncPaymentIntentResponse>.FromFailure(committed);

        return Result<SyncPaymentIntentResponse>.Success(new SyncPaymentIntentResponse(true));
    }
}
