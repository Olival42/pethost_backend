using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.Hosts.StripeOnboarding;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Seed;

/// <summary>
/// Cria o anfitrião de demonstração (<see cref="DemoHostSeedOptions"/>) se ainda não existir, pelo
/// <see cref="VerifiedHostSeeder"/>: a conta, uma solicitação aprovada e o perfil de anfitrião.
/// </summary>
/// <remarks>
/// Idempotente pelo e-mail: se a conta de anfitrião já existe, não faz nada (nem troca senha,
/// como o seed do admin).
/// <para>
/// Com conta conectada configurada, lê a situação dela no Stripe logo depois de criar (se o
/// Stripe estiver configurado e no ar): conta de teste já aprovada põe os cantinhos na busca sem
/// esperar webhook. Falhar aqui não impede a subida.
/// </para>
/// </remarks>
internal sealed partial class HostsDbSeeder(
    VerifiedHostSeeder verifiedHostSeeder,
    HostPayoutSetup payoutSetup,
    IOptions<DemoHostSeedOptions> options,
    ILogger<HostsDbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(settings.Email) || string.IsNullOrWhiteSpace(settings.Password) || string.IsNullOrWhiteSpace(settings.Cpf))
        {
            LogIncomplete(logger);
            return;
        }

        var registration = new AccountRegistration(
            settings.FullName,
            settings.Email,
            settings.Password,
            Roles.Host,
            settings.Phone,
            settings.BirthDate,
            new AddressData(settings.ZipCode, settings.Street, settings.Number, null, settings.Neighborhood, settings.City, settings.State));

        var result = await verifiedHostSeeder
            .SeedAsync(new VerifiedHostSeed(registration, settings.Cpf, settings.StripeAccountId), cancellationToken)
            .ConfigureAwait(false);

        switch (result.Status)
        {
            case VerifiedHostSeedStatus.InvalidCpf:
                LogInvalidCpf(logger);
                return;
            case VerifiedHostSeedStatus.AlreadyExists:
                LogAlreadyExists(logger);
                return;
            case VerifiedHostSeedStatus.Rejected:
                LogRejected(logger, result.Detail ?? "unknown");
                return;
            case VerifiedHostSeedStatus.CpfInUse:
                LogCpfInUse(logger);
                return;
            case VerifiedHostSeedStatus.Failed:
                LogFailed(logger);
                return;
        }

        var host = result.Host!;
        var stripeLinked = host.StripeAccountId is not null;
        LogCreated(logger, stripeLinked);

        if (stripeLinked)
        {
            var synced = await payoutSetup.SyncAsync(host, cancellationToken).ConfigureAwait(false);
            LogStripeSynced(logger, synced.IsSuccess ? host.StripePayoutsEnabled.ToString() : synced.FirstError?.Code ?? "unknown");
        }
    }

    [LoggerMessage(EventId = 7100, Level = LogLevel.Warning, Message = "Demo host seed is enabled but Email, Password or Cpf is missing; skipped.")]
    private static partial void LogIncomplete(ILogger logger);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "Demo host seed: the configured CPF is invalid; skipped.")]
    private static partial void LogInvalidCpf(ILogger logger);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Information, Message = "Demo host already exists; nothing to seed.")]
    private static partial void LogAlreadyExists(ILogger logger);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Warning, Message = "Demo host seed rejected by the account rules: {Fields}.")]
    private static partial void LogRejected(ILogger logger, string fields);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Warning, Message = "Demo host seed: the configured CPF already belongs to another host; skipped.")]
    private static partial void LogCpfInUse(ILogger logger);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Error, Message = "Demo host seed could not save the host profile; the account was removed.")]
    private static partial void LogFailed(ILogger logger);

    [LoggerMessage(EventId = 7106, Level = LogLevel.Information, Message = "Demo host created (Stripe account linked: {StripeLinked}).")]
    private static partial void LogCreated(ILogger logger, bool stripeLinked);

    [LoggerMessage(EventId = 7107, Level = LogLevel.Information, Message = "Demo host Stripe account synced (payouts enabled / error: {Outcome}).")]
    private static partial void LogStripeSynced(ILogger logger, string outcome);
}
