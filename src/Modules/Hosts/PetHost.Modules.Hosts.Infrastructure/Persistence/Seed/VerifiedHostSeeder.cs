using PetHost.Modules.Hosts.Application.HostApplications;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Seed;

/// <summary>Anfitrião que o seed cria já verificado.</summary>
/// <param name="Account">A conta, com o papel <c>host</c>.</param>
/// <param name="Cpf">CPF fictício, com dígitos verificadores válidos.</param>
/// <param name="StripeAccountId">Conta conectada de verdade (<c>acct_...</c>), criada à mão no Dashboard. Opcional.</param>
/// <param name="SimulatedPayouts">
/// Sem Stripe: liga uma conta conectada de mentira (<c>acct_seed...</c>) e já marca os repasses
/// como liberados, para os cantinhos aparecerem na busca. Pagamento para ela não funciona.
/// </param>
public sealed record VerifiedHostSeed(
    AccountRegistration Account,
    string Cpf,
    string? StripeAccountId = null,
    bool SimulatedPayouts = false);

/// <summary>O que aconteceu com um <see cref="VerifiedHostSeed"/>.</summary>
public enum VerifiedHostSeedStatus
{
    Created,

    /// <summary>Já existe conta de anfitrião com esse e-mail: nada foi feito.</summary>
    AlreadyExists,

    InvalidCpf,

    /// <summary>O CPF já é de outro anfitrião ou de uma solicitação em aberto.</summary>
    CpfInUse,

    /// <summary>A conta não passou nas regras do cadastro. <see cref="VerifiedHostSeedResult.Detail"/> diz os campos.</summary>
    Rejected,

    /// <summary>Não deu para gravar o perfil de anfitrião; a conta foi desfeita.</summary>
    Failed,
}

/// <param name="UserId">A conta criada. Só com <see cref="VerifiedHostSeedStatus.Created"/>.</param>
/// <param name="Host">O anfitrião criado. Só com <see cref="VerifiedHostSeedStatus.Created"/>.</param>
public sealed record VerifiedHostSeedResult(
    VerifiedHostSeedStatus Status,
    Guid? UserId = null,
    Host? Host = null,
    string? Detail = null);

/// <summary>
/// Cria um anfitrião já verificado: a conta (pelo contrato do Auth), uma solicitação aprovada e o
/// perfil de anfitrião. Usado pelo anfitrião de demonstração e pelos dados de teste.
/// </summary>
/// <remarks>
/// Passa pelo mesmo fluxo do domínio — abre, "envia" as fotos, envia, aprova — com fotos de
/// mentira que nunca vão ao bucket e são apagadas na hora. Assim não existe atalho no domínio
/// para criar anfitrião sem verificação. Idempotente pelo e-mail: conta de anfitrião que já existe
/// fica como está (nem troca senha).
/// </remarks>
public sealed class VerifiedHostSeeder(
    IAccountRegistrar accountRegistrar,
    IHostRepository hostRepository,
    IHostApplicationRepository applicationRepository,
    IHostsUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>"Quem aprovou" os anfitriões do seed: não é um admin de verdade.</summary>
    public static readonly Guid SeedActorId = new("5eed0000-0000-7000-8000-000000000001");

    /// <summary>Prefixo das contas conectadas de mentira (<see cref="VerifiedHostSeed.SimulatedPayouts"/>).</summary>
    public const string SimulatedStripeAccountPrefix = "acct_seed";

    private const string EmailAlreadyRegistered = "AUTH_EMAIL_ALREADY_REGISTERED";

    public async Task<VerifiedHostSeedResult> SeedAsync(VerifiedHostSeed seed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var cpf = Cpf.Create(seed.Cpf);
        if (cpf.IsFailure)
            return new(VerifiedHostSeedStatus.InvalidCpf);

        var valid = await accountRegistrar.ValidateAsync(seed.Account, cancellationToken).ConfigureAwait(false);
        if (valid.IsFailure)
        {
            return valid.FirstError?.Code == EmailAlreadyRegistered
                ? new(VerifiedHostSeedStatus.AlreadyExists)
                : new(VerifiedHostSeedStatus.Rejected, Detail: string.Join(", ", valid.Errors!.Select(e => e.Field ?? e.Code)));
        }

        var cpfInUse = await hostRepository.ExistsIndividualByCpfAsync(cpf.Value!, cancellationToken).ConfigureAwait(false)
            || await applicationRepository.ExistsOpenWithCpfAsync(cpf.Value!, Guid.Empty, cancellationToken).ConfigureAwait(false);
        if (cpfInUse)
            return new(VerifiedHostSeedStatus.CpfInUse);

        var session = await accountRegistrar.RegisterAsync(seed.Account, cancellationToken).ConfigureAwait(false);
        if (session.IsFailure)
            return new(VerifiedHostSeedStatus.Rejected, Detail: session.FirstError?.Code ?? "unknown");

        var userId = session.Value!.User.Id;
        var host = await CreateVerifiedHostAsync(userId, cpf.Value!, seed, cancellationToken).ConfigureAwait(false);

        if (host is null)
        {
            // Compensação: sem perfil de anfitrião, a conta não fica pela metade.
            await accountRegistrar.DeleteAsync(userId, CancellationToken.None).ConfigureAwait(false);
            return new(VerifiedHostSeedStatus.Failed);
        }

        return new(VerifiedHostSeedStatus.Created, userId, host);
    }

    private async Task<Host?> CreateVerifiedHostAsync(Guid userId, Cpf cpf, VerifiedHostSeed seed, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var started = HostApplication.Start([], userId, cpf, IdentityDocumentType.Cin, company: null, HostVerificationNotice.CurrentVersion, null, now);
        if (started.IsFailure)
            return null;

        var application = started.Value!;

        // Fotos de mentira: só para o fluxo do domínio. Nunca existiram no bucket.
        foreach (var (kind, index) in DocumentKindValues.Required.Select((kind, index) => (kind, index)))
            application.AttachDocument(kind, $"seed/host/{DocumentKindValues.ToWire(kind)}", new string((char)('a' + index), 64), 0, now);

        if (application.Submit(now).IsFailure)
            return null;

        var approved = application.Approve(SeedActorId, "Anfitrião criado pelo seed.", now);
        if (approved.IsFailure)
            return null;

        application.PurgeDocuments(now);

        var host = approved.Value!;
        if (!LinkStripe(host, seed, cpf, now))
            return null;

        applicationRepository.Add(application);
        hostRepository.Add(host);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return saved.IsSuccess ? host : null;
    }

    private static bool LinkStripe(Host host, VerifiedHostSeed seed, Cpf cpf, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(seed.StripeAccountId))
            return host.LinkStripeAccount(seed.StripeAccountId, now).IsSuccess;

        if (!seed.SimulatedPayouts)
            return true;

        // Um id por CPF: a conta conectada é única entre os anfitriões.
        if (host.LinkStripeAccount($"{SimulatedStripeAccountPrefix}{cpf.Value}", now).IsFailure)
            return false;

        host.ApplyStripeSnapshot(new StripeAccountSnapshot(DetailsSubmitted: true, ChargesEnabled: true, PayoutsEnabled: true, DisabledReason: null), now);
        return true;
    }
}
