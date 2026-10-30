using Microsoft.Extensions.Logging;
using PetHost.Modules.Hosts.Application.Hosts.SyncStripeAccount;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Shared.Infrastructure.Stripe;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Hosts.Infrastructure.Payouts;

/// <summary>
/// Webhook <c>account.updated</c> (contas conectadas): o Stripe avisa que a conta de um anfitrião
/// mudou — terminou o cadastro, foi aprovada, foi bloqueada. O evento só serve de aviso: a conta
/// é relida na API (<see cref="SyncStripeAccountCommand"/>).
/// </summary>
/// <remarks>
/// Conta que não é de nenhum anfitrião: ignorada. Stripe fora do ar na releitura: exceção → 500,
/// e o Stripe reenvia o evento mais tarde. Outras falhas vão para o log e não são reenviadas.
/// </remarks>
internal sealed partial class HostAccountUpdatedWebhookHandler(
    ICommandHandler<SyncStripeAccountCommand, Unit> syncStripeAccount,
    ILogger<HostAccountUpdatedWebhookHandler> logger) : IStripeWebhookHandler
{
    public const string EventType = "account.updated";

    public bool CanHandle(string eventType) => eventType == EventType;

    public async Task HandleAsync(StripeWebhookEvent stripeEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stripeEvent);

        var accountId = stripeEvent.ConnectedAccountId ?? stripeEvent.ObjectId;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            LogMissingAccount(logger, stripeEvent.Id);
            return;
        }

        var result = await syncStripeAccount.HandleAsync(new SyncStripeAccountCommand(accountId), cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
            return;

        var code = result.FirstError?.Code;
        if (code == HostsErrors.StripeAccountNotFound.Code)
        {
            LogUnknownAccount(logger, accountId, stripeEvent.Id);
            return;
        }

        if (code == HostsErrors.StripeUnavailable.Code)
            throw new InvalidOperationException($"Stripe was unavailable while syncing account '{accountId}' (event '{stripeEvent.Id}').");

        LogSyncFailed(logger, accountId, stripeEvent.Id, code ?? "unknown");
    }

    [LoggerMessage(EventId = 7210, Level = LogLevel.Warning, Message = "Stripe event {EventId} (account.updated) has no account id; ignored.")]
    private static partial void LogMissingAccount(ILogger logger, string eventId);

    [LoggerMessage(EventId = 7211, Level = LogLevel.Information, Message = "Stripe account {StripeAccountId} (event {EventId}) does not belong to any host; ignored.")]
    private static partial void LogUnknownAccount(ILogger logger, string stripeAccountId, string eventId);

    [LoggerMessage(EventId = 7212, Level = LogLevel.Warning, Message = "Stripe account {StripeAccountId} (event {EventId}) could not be synced: {ErrorCode}.")]
    private static partial void LogSyncFailed(ILogger logger, string stripeAccountId, string eventId, string errorCode);
}
