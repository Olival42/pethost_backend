using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Infrastructure.Stripe;
using PetHost.Shared.Kernel.Results;
using Stripe;
using StripeV2 = Stripe.V2.Core;

namespace PetHost.Modules.Hosts.Infrastructure.Payouts;

/// <summary>
/// Adaptador da porta <see cref="IStripeConnect"/> sobre o SDK do Stripe (Connect).
/// </summary>
/// <remarks>
/// <para>
/// <b>Conta conectada</b> criada pela <b>Accounts v2</b> (<c>POST /v2/core/accounts</c>), o caminho
/// que o Stripe exige para integração nova: configuração <c>recipient</c> com
/// <c>stripe_balance.stripe_transfers</c> — o PetHost cobra o tutor e transfere para o anfitrião
/// (cobrança e transferência separadas) — mais <c>merchant</c> com <c>card_payments</c>, que o
/// Stripe exige junto com as transferências numa conta brasileira; painel Express (o anfitrião vê os repasses no painel
/// simples do Stripe), país <c>BR</c>. Taxas e perdas (estorno, chargeback) ficam com a plataforma
/// (<c>defaults.responsibilities</c> = <c>application</c>, obrigatório com o painel Express).
/// </para>
/// <para>
/// O resto continua nos endpoints v1, que o Stripe mantém compatíveis com contas v2: leitura da
/// conta (<c>GET /v1/accounts/{id}</c>, com <c>payouts_enabled</c> etc.), sessão do componente
/// embutido, link do onboarding hospedado e o webhook <c>account.updated</c>.
/// </para>
/// <para>
/// <b>Pré-preenchimento:</b> pessoa física vai como <c>individual</c> (nome, nascimento, CPF
/// <c>br_cpf</c>, e-mail, telefone, endereço); empresa como <c>company</c> (razão social, CNPJ
/// <c>br_cnpj</c>, telefone, endereço da empresa) mais o representante legal como <c>person</c>. O
/// resto (conta bancária, documento, sócios) o Stripe pede no onboarding dele.
/// </para>
/// </remarks>
internal sealed partial class StripeConnectGateway(
    IStripeClientProvider clientProvider,
    IOptions<StripeOptions> options,
    ILogger<StripeConnectGateway> logger) : IStripeConnect
{
    private const string Country = "BR";
    private const string PhoneCountryCode = "+55";

    // Valores de teste do Stripe (https://docs.stripe.com/connect/testing), usados só com chave de teste.
    internal const string TestAddressLine1 = "address_full_match";
    internal const string TestIdentityDocument = "file_identity_document_success";
    internal static readonly DateOnly TestBirthDate = new(1901, 1, 1);

    public async Task<Result<string>> CreateAccountAsync(StripeAccountDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (clientProvider.Client is not StripeClient client)
            return NotConfigured<string>();

        var testData = options.Value.UsesTestOnboardingData;
        if (testData)
        {
            draft = WithTestVerificationData(draft);
            LogTestData(logger, draft.HostId);
        }

        var isCompany = draft.PersonType == PersonType.Company;
        var accountOptions = new StripeV2.AccountCreateOptions
        {
            ContactEmail = draft.Email,
            DisplayName = isCompany ? draft.TradeName : draft.FullName,
            Dashboard = "express",
            Defaults = new StripeV2.AccountCreateDefaultsOptions
            {
                Responsibilities = new StripeV2.AccountCreateDefaultsResponsibilitiesOptions
                {
                    FeesCollector = "application",
                    LossesCollector = "application",
                },
            },
            Configuration = new StripeV2.AccountCreateConfigurationOptions
            {
                // No Brasil o Stripe só libera stripe_transfers junto com card_payments (merchant).
                Merchant = new StripeV2.AccountCreateConfigurationMerchantOptions
                {
                    Capabilities = new StripeV2.AccountCreateConfigurationMerchantCapabilitiesOptions
                    {
                        CardPayments = new StripeV2.AccountCreateConfigurationMerchantCapabilitiesCardPaymentsOptions { Requested = true },
                    },
                },
                Recipient = new StripeV2.AccountCreateConfigurationRecipientOptions
                {
                    Capabilities = new StripeV2.AccountCreateConfigurationRecipientCapabilitiesOptions
                    {
                        StripeBalance = new StripeV2.AccountCreateConfigurationRecipientCapabilitiesStripeBalanceOptions
                        {
                            StripeTransfers = new StripeV2.AccountCreateConfigurationRecipientCapabilitiesStripeBalanceStripeTransfersOptions { Requested = true },
                        },
                    },
                },
            },
            Identity = isCompany ? CompanyIdentity(draft) : IndividualIdentity(draft, testData),
            Metadata = new Dictionary<string, string>
            {
                ["host_id"] = draft.HostId.ToString(),
                ["user_id"] = draft.UserId.ToString(),
            },
        };

        // Mesma chave para o mesmo anfitrião: clique duplo ou nova tentativa devolvem a mesma
        // conta, em vez de criar duas.
        // Depois de o admin desligar uma conta recusada, a tentativa muda e vira uma conta nova.
        var requestOptions = new RequestOptions { IdempotencyKey = $"pethost-host-{draft.HostId:N}-connected-account-v2-{draft.Attempt}" };

        StripeV2.Account account;
        try
        {
            account = await client.V2.Core.Accounts
                .CreateAsync(accountOptions, requestOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (StripeException exception)
        {
            return Failure<string>(exception, "create account");
        }

        if (isCompany)
            await AddRepresentativeAsync(client, account.Id, draft, testData, cancellationToken).ConfigureAwait(false);

        LogAccountCreated(logger, account.Id, draft.HostId);
        return Result<string>.Success(account.Id);
    }

    /// <summary>
    /// Modo de teste: nascimento e endereço trocados pelos valores que o Stripe de teste dá como
    /// "confere" (<c>1901-01-01</c>, <c>address_full_match</c> com cidade, UF e CEP reais). CPF, CNPJ,
    /// nome e telefone continuam os do cadastro — o Stripe não tem valor de teste para CPF/CNPJ; o
    /// documento de teste aprovado (<see cref="TestIdentityDocument"/>) cobre a identidade.
    /// </summary>
    internal static StripeAccountDraft WithTestVerificationData(StripeAccountDraft draft) =>
        draft with
        {
            BirthDate = TestBirthDate,
            Address = TestAddress(draft.Address),
            CompanyAddress = TestAddress(draft.CompanyAddress),
        };

    private static AddressData? TestAddress(AddressData? address) =>
        address is null ? null : address with { Street = TestAddressLine1, Number = null, Complement = null };

    private static StripeV2.AccountCreateIdentityOptions IndividualIdentity(StripeAccountDraft draft, bool testData)
    {
        var (firstName, lastName) = SplitName(draft.FullName);
        var address = Address(draft.Address);

        return new StripeV2.AccountCreateIdentityOptions
        {
            Country = Country,
            EntityType = "individual",
            Individual = new StripeV2.AccountCreateIdentityIndividualOptions
            {
                GivenName = firstName,
                Surname = lastName,
                Email = draft.Email,
                Phone = Phone(draft.Phone),
                IdNumbers = [new() { Type = "br_cpf", Value = draft.Cpf }],
                DateOfBirth = draft.BirthDate is { } date ? new() { Day = date.Day, Month = date.Month, Year = date.Year } : null,
                Address = address is null
                    ? null
                    : new() { Line1 = address.Line1, Line2 = address.Line2, City = address.City, State = address.State, PostalCode = address.PostalCode, Country = Country },
                Documents = testData
                    ? new()
                    {
                        PrimaryVerification = new() { Type = "front_back", FrontBack = new() { Front = TestIdentityDocument } },
                    }
                    : null,
            },
        };
    }

    private static StripeV2.AccountCreateIdentityOptions CompanyIdentity(StripeAccountDraft draft)
    {
        var address = Address(draft.CompanyAddress);

        return new StripeV2.AccountCreateIdentityOptions
        {
            Country = Country,
            EntityType = "company",
            BusinessDetails = new StripeV2.AccountCreateIdentityBusinessDetailsOptions
            {
                RegisteredName = draft.LegalName,
                Phone = Phone(draft.Phone),
                IdNumbers = [new() { Type = "br_cnpj", Value = draft.Cnpj! }],
                Address = address is null
                    ? null
                    : new() { Line1 = address.Line1, Line2 = address.Line2, City = address.City, State = address.State, PostalCode = address.PostalCode, Country = Country },
            },
        };
    }

    public async Task<Result<StripeAccountSnapshot>> GetAccountAsync(string stripeAccountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeAccountId);

        if (clientProvider.Client is not { } client)
            return NotConfigured<StripeAccountSnapshot>();

        try
        {
            var account = await new AccountService(client)
                .GetAsync(stripeAccountId, options: null, requestOptions: null, cancellationToken)
                .ConfigureAwait(false);

            return Result<StripeAccountSnapshot>.Success(new StripeAccountSnapshot(
                account.DetailsSubmitted,
                account.ChargesEnabled,
                account.PayoutsEnabled,
                account.Requirements?.DisabledReason));
        }
        catch (StripeException exception)
        {
            return Failure<StripeAccountSnapshot>(exception, "read account");
        }
    }

    public async Task<Result<StripeOnboardingSession>> CreateOnboardingSessionAsync(string stripeAccountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeAccountId);

        if (clientProvider.Client is not { } client)
            return NotConfigured<StripeOnboardingSession>();

        var sessionOptions = new AccountSessionCreateOptions
        {
            Account = stripeAccountId,
            Components = new AccountSessionComponentsOptions
            {
                AccountOnboarding = new AccountSessionComponentsAccountOnboardingOptions { Enabled = true },
            },
        };

        try
        {
            var session = await new AccountSessionService(client)
                .CreateAsync(sessionOptions, requestOptions: null, cancellationToken)
                .ConfigureAwait(false);

            return Result<StripeOnboardingSession>.Success(
                new StripeOnboardingSession(session.ClientSecret, clientProvider.PublishableKey, Utc(session.ExpiresAt)));
        }
        catch (StripeException exception)
        {
            return Failure<StripeOnboardingSession>(exception, "create account session");
        }
    }

    public async Task<Result<StripeOnboardingLink>> CreateOnboardingLinkAsync(string stripeAccountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeAccountId);

        if (clientProvider.Client is not { } client)
            return NotConfigured<StripeOnboardingLink>();

        var linkOptions = new AccountLinkCreateOptions
        {
            Account = stripeAccountId,
            Type = "account_onboarding",
            ReturnUrl = options.Value.ConnectReturnUrl,
            RefreshUrl = options.Value.ConnectRefreshUrl,

            // Pede de uma vez tudo o que o Stripe vai exigir, não só o mínimo de agora: menos
            // idas e vindas para quem não tem intimidade com o celular.
            CollectionOptions = new AccountLinkCollectionOptionsOptions { Fields = "eventually_due" },
        };

        try
        {
            var link = await new AccountLinkService(client)
                .CreateAsync(linkOptions, requestOptions: null, cancellationToken)
                .ConfigureAwait(false);

            return Result<StripeOnboardingLink>.Success(new StripeOnboardingLink(link.Url, Utc(link.ExpiresAt)));
        }
        catch (StripeException exception)
        {
            return Failure<StripeOnboardingLink>(exception, "create account link");
        }
    }

    /// <summary>
    /// Representante legal da empresa (a dona da conta PetHost, com o CPF conferido na
    /// verificação). Se o Stripe recusar o pré-preenchimento, segue: o onboarding pede os dados.
    /// </summary>
    private async Task AddRepresentativeAsync(IStripeClient client, string accountId, StripeAccountDraft draft, bool testData, CancellationToken cancellationToken)
    {
        var (firstName, lastName) = SplitName(draft.FullName);
        var personOptions = new AccountPersonCreateOptions
        {
            FirstName = firstName,
            LastName = lastName,
            Email = draft.Email,
            Phone = Phone(draft.Phone),
            IdNumber = draft.Cpf,
            Dob = draft.BirthDate is { } date ? new AccountPersonDobOptions { Day = date.Day, Month = date.Month, Year = date.Year } : null,
            Address = Address(draft.Address),
            Relationship = new AccountPersonRelationshipOptions { Representative = true },
            Verification = testData
                ? new AccountPersonVerificationOptions { Document = new AccountPersonVerificationDocumentOptions { Front = TestIdentityDocument } }
                : null,
        };

        try
        {
            await new AccountPersonService(client)
                .CreateAsync(accountId, personOptions, requestOptions: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (StripeException exception)
        {
            LogRepresentativeSkipped(logger, accountId, exception.StripeError?.Code ?? exception.HttpStatusCode.ToString());
        }
    }

    private Result<T> NotConfigured<T>()
    {
        LogNotConfigured(logger);
        return Result<T>.Failure(HostsErrors.StripeUnavailable);
    }

    /// <summary>
    /// Pedido recusado pelo Stripe (400, 402, 404) é dado que ele não aceita: 422. O resto — fora do
    /// ar, lento, limite, chave errada — é indisponibilidade: 503, tente de novo depois.
    /// </summary>
    private Result<T> Failure<T>(StripeException exception, string operation)
    {
        var rejected = exception.HttpStatusCode is HttpStatusCode.BadRequest or HttpStatusCode.PaymentRequired or HttpStatusCode.NotFound
            && exception.StripeError?.Type != "idempotency_error";

        var code = exception.StripeError?.Code ?? exception.StripeError?.Type ?? "none";

        if (rejected)
        {
            LogRejected(logger, operation, (int)exception.HttpStatusCode, code, exception.StripeError?.Param, exception.StripeError?.Message);
            return Result<T>.Failure(HostsErrors.StripeRequestRejected);
        }

        LogUnavailable(logger, exception, operation, (int)exception.HttpStatusCode, code);
        return Result<T>.Failure(HostsErrors.StripeUnavailable);
    }

    /// <summary>Primeiro nome e o resto (sobrenomes), como o Stripe pede.</summary>
    internal static (string FirstName, string? LastName) SplitName(string fullName)
    {
        var parts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => (fullName.Trim(), null),
            1 => (parts[0], null),
            _ => (parts[0], parts[1]),
        };
    }

    /// <summary>Telefone guardado só com dígitos (DDD + número) → E.164 (<c>+5544999998888</c>).</summary>
    internal static string? Phone(string? digits) =>
        string.IsNullOrWhiteSpace(digits) ? null : PhoneCountryCode + digits.Trim();

    /// <summary>Endereço do PetHost → endereço do Stripe: <c>line1</c> = rua + número, <c>line2</c> = complemento.</summary>
    internal static AddressOptions? Address(AddressData? address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Street))
            return null;

        return new AddressOptions
        {
            Line1 = string.IsNullOrWhiteSpace(address.Number) ? address.Street : $"{address.Street}, {address.Number}",
            Line2 = string.IsNullOrWhiteSpace(address.Complement) ? null : address.Complement,
            City = address.City,
            State = address.State,
            PostalCode = ZipCode(address.ZipCode),
            Country = Country,
        };
    }

    private static string? ZipCode(string? digits) =>
        digits is { Length: 8 } ? string.Create(CultureInfo.InvariantCulture, $"{digits[..5]}-{digits[5..]}") : digits;

    private static DobOptions? Dob(DateOnly? birthDate) =>
        birthDate is { } date ? new DobOptions { Day = date.Day, Month = date.Month, Year = date.Year } : null;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    [LoggerMessage(EventId = 7205, Level = LogLevel.Information, Message = "Stripe test mode: payout setup for host {HostId} sent with Stripe test verification data.")]
    private static partial void LogTestData(ILogger logger, Guid hostId);

    [LoggerMessage(EventId = 7200, Level = LogLevel.Information, Message = "Stripe connected account {StripeAccountId} created for host {HostId}.")]
    private static partial void LogAccountCreated(ILogger logger, string stripeAccountId, Guid hostId);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Warning, Message = "Stripe is not configured (Stripe:SecretKey is empty); payout setup is unavailable.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Warning, Message = "Stripe rejected '{Operation}': HTTP {StatusCode}, code {StripeCode}, param {StripeParam}: {StripeMessage}")]
    private static partial void LogRejected(ILogger logger, string operation, int statusCode, string stripeCode, string? stripeParam, string? stripeMessage);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Error, Message = "Stripe unavailable on '{Operation}': HTTP {StatusCode}, code {StripeCode}.")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string operation, int statusCode, string stripeCode);

    [LoggerMessage(EventId = 7204, Level = LogLevel.Warning, Message = "Stripe did not accept the representative prefill for {StripeAccountId} ({StripeCode}); the onboarding will ask for it.")]
    private static partial void LogRepresentativeSkipped(ILogger logger, string stripeAccountId, string stripeCode);
}
