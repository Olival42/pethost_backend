using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments;

/// <summary>
/// Grava o pagamento e, só se gravou, publica os efeitos (trilha e avisos). Conflito de gravação
/// volta como <c>409 PAYMENT_CONFLICT</c> e nada é anunciado.
/// </summary>
public sealed class PaymentCommitter(IPaymentsUnitOfWork unitOfWork, PaymentEffectsPublisher publisher)
{
    public async Task<Result> CommitAsync(Payment payment, PaymentEffects effects, CancellationToken cancellationToken)
    {
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return saved;

        await publisher.PublishAsync(payment, effects, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
