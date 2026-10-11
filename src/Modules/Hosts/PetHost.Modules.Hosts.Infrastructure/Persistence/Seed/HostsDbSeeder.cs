using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.HostApplications;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Seed;

/// <summary>
/// Cria o anfitrião de demonstração (<see cref="DemoHostSeedOptions"/>) se ainda não existir:
/// a conta (pelo contrato do Auth), uma solicitação aprovada e o perfil de anfitrião.
/// </summary>
/// <remarks>
/// Passa pelo mesmo fluxo do domínio — abre, "envia" as fotos, envia, aprova — com fotos de
/// mentira que nunca vão ao bucket e são apagadas na hora. Assim não existe atalho no domínio
/// para criar anfitrião sem verificação.
/// <para>
/// Idempotente pelo e-mail: se a conta de anfitrião já existe, não faz nada (nem troca senha,
/// como o seed do admin).
/// </para>
/// </remarks>
internal sealed partial class HostsDbSeeder(
    IAccountRegistrar accountRegistrar,
    IHostRepository hostRepository,
    IHostApplicationRepository applicationRepository,
    IHostsUnitOfWork unitOfWork,
    IOptions<DemoHostSeedOptions> options,
    TimeProvider timeProvider,
    ILogger<HostsDbSeeder> logger)
{
    /// <summary>"Quem aprovou" o anfitrião de demonstração: não é um admin de verdade.</summary>
    public static readonly Guid SeedActorId = new("5eed0000-0000-7000-8000-000000000001");

    private const string EmailAlreadyRegistered = "AUTH_EMAIL_ALREADY_REGISTERED";

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

        var cpf = Cpf.Create(settings.Cpf);
        if (cpf.IsFailure)
        {
            LogInvalidCpf(logger);
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

        var valid = await accountRegistrar.ValidateAsync(registration, cancellationToken).ConfigureAwait(false);
        if (valid.IsFailure)
        {
            if (valid.FirstError?.Code == EmailAlreadyRegistered)
                LogAlreadyExists(logger);
            else
                LogRejected(logger, string.Join(", ", valid.Errors!.Select(e => e.Field ?? e.Code)));

            return;
        }

        var cpfInUse = await hostRepository.ExistsIndividualByCpfAsync(cpf.Value!, cancellationToken).ConfigureAwait(false)
            || await applicationRepository.ExistsOpenWithCpfAsync(cpf.Value!, Guid.Empty, cancellationToken).ConfigureAwait(false);
        if (cpfInUse)
        {
            LogCpfInUse(logger);
            return;
        }

        var session = await accountRegistrar.RegisterAsync(registration, cancellationToken).ConfigureAwait(false);
        if (session.IsFailure)
        {
            var code = session.FirstError?.Code ?? "unknown";
            LogRejected(logger, code);
            return;
        }

        var userId = session.Value!.User.Id;
        var saved = await CreateVerifiedHostAsync(userId, cpf.Value!, settings, cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            // Compensação: sem perfil de anfitrião, a conta não fica pela metade.
            await accountRegistrar.DeleteAsync(userId, CancellationToken.None).ConfigureAwait(false);
            LogFailed(logger);
            return;
        }

        var stripeLinked = !string.IsNullOrWhiteSpace(settings.StripeAccountId);
        LogCreated(logger, stripeLinked);
    }

    private async Task<bool> CreateVerifiedHostAsync(Guid userId, Cpf cpf, DemoHostSeedOptions settings, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var started = HostApplication.Start([], userId, cpf, IdentityDocumentType.Cin, company: null, HostVerificationNotice.CurrentVersion, null, now);
        if (started.IsFailure)
            return false;

        var application = started.Value!;

        // Fotos de mentira: só para o fluxo do domínio. Nunca existiram no bucket.
        foreach (var (kind, index) in DocumentKindValues.Required.Select((kind, index) => (kind, index)))
            application.AttachDocument(kind, $"seed/demo-host/{DocumentKindValues.ToWire(kind)}", new string((char)('a' + index), 64), 0, now);

        if (application.Submit(now).IsFailure)
            return false;

        var approved = application.Approve(SeedActorId, "Anfitrião de demonstração criado pelo seed.", now);
        if (approved.IsFailure)
            return false;

        application.PurgeDocuments(now);

        var host = approved.Value!;
        if (!string.IsNullOrWhiteSpace(settings.StripeAccountId))
            host.LinkStripeAccount(settings.StripeAccountId, now);

        applicationRepository.Add(application);
        hostRepository.Add(host);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return saved.IsSuccess;
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
}
