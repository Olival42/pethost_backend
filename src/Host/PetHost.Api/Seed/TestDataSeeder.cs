using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Infrastructure.Persistence.Seed;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Infrastructure.Persistence.Seed;
using PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;
using PetHost.Modules.Pets.Application.Pets.RegisterPet;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Api.Seed;

/// <summary>
/// Cria os dados de teste do <see cref="TestDataCatalog"/>: tutores com os pets deles e
/// anfitriões já verificados, com cantinho e os pets da casa.
/// </summary>
/// <remarks>
/// Cruza módulos, por isso mora na API (a composição): cada parte entra pelo fluxo do próprio
/// módulo — o cadastro de tutor e o de pet pelos handlers da API (com a validação), o anfitrião
/// pela verificação aprovada do Hosts, o cantinho pelo domínio do Listings com a coordenada
/// pronta (sem consultar o mapa).
/// <para>
/// Os anfitriões ficam com uma conta conectada de mentira e os repasses liberados, para os
/// cantinhos aparecerem na busca sem passar pelo Stripe. Pedir e aceitar reserva funciona;
/// <b>pagar não</b> (o Stripe não conhece a conta) — para o pagamento, use o anfitrião de
/// demonstração com uma conta conectada de verdade.
/// </para>
/// <para>
/// Idempotente pelo e-mail: pessoa que já tem a conta fica como está, sem pets nem cantinho
/// novos. Falhar numa pessoa não impede as outras nem a subida.
/// </para>
/// </remarks>
public sealed partial class TestDataSeeder(
    ICommandHandler<RegisterOwnerAccountCommand, OwnerSessionResponse> registerOwner,
    ICommandHandler<RegisterPetCommand, PetResponse> registerPet,
    VerifiedHostSeeder verifiedHostSeeder,
    ListingSeeder listingSeeder,
    ILogger<TestDataSeeder> logger)
{
    private const string EmailAlreadyRegistered = "AUTH_EMAIL_ALREADY_REGISTERED";

    /// <summary>Cria o que falta do catálogo, todas as contas com <paramref name="password"/>.</summary>
    public async Task SeedAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        foreach (var owner in TestDataCatalog.Owners)
            await SeedOwnerAsync(owner, password, cancellationToken).ConfigureAwait(false);

        foreach (var host in TestDataCatalog.Hosts)
            await SeedHostAsync(host, password, cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedOwnerAsync(TestOwner owner, string password, CancellationToken cancellationToken)
    {
        var person = owner.Person;
        var registered = await registerOwner
            .HandleAsync(
                new RegisterOwnerAccountCommand(person.FullName, person.Email, password, person.Phone, person.BirthDate, person.Cpf, person.Address),
                cancellationToken)
            .ConfigureAwait(false);

        if (registered.IsFailure)
        {
            if (registered.Errors!.Any(e => e.Code == EmailAlreadyRegistered))
                LogAlreadyExists(logger, Roles.Owner, person.Email);
            else
                LogRejected(logger, Roles.Owner, person.Email, Describe(registered));

            return;
        }

        var userId = registered.Value!.Owner.UserId;
        var pets = await SeedPetsAsync(userId, Roles.Owner, person.Email, owner.Pets, cancellationToken).ConfigureAwait(false);

        LogOwnerCreated(logger, person.Email, pets);
    }

    private async Task SeedHostAsync(TestHost host, string password, CancellationToken cancellationToken)
    {
        var person = host.Person;
        var registration = new AccountRegistration(person.FullName, person.Email, password, Roles.Host, person.Phone, person.BirthDate, person.Address);

        var created = await verifiedHostSeeder
            .SeedAsync(new VerifiedHostSeed(registration, person.Cpf, SimulatedPayouts: true), cancellationToken)
            .ConfigureAwait(false);

        if (created.Status == VerifiedHostSeedStatus.AlreadyExists)
        {
            LogAlreadyExists(logger, Roles.Host, person.Email);
            return;
        }

        if (created.Status != VerifiedHostSeedStatus.Created)
        {
            LogRejected(logger, Roles.Host, person.Email, created.Detail is null ? created.Status.ToString() : $"{created.Status}: {created.Detail}");
            return;
        }

        var userId = created.UserId!.Value;
        var listing = await listingSeeder
            .SeedAsync(userId, ToListingSeed(host.Listing, person.Address), cancellationToken)
            .ConfigureAwait(false);

        if (listing.IsFailure)
            LogListingRejected(logger, person.Email, Describe(listing));

        var pets = await SeedPetsAsync(userId, Roles.Host, person.Email, host.Pets, cancellationToken).ConfigureAwait(false);

        LogHostCreated(logger, person.Email, listing.IsSuccess, pets);
    }

    private async Task<int> SeedPetsAsync(Guid userId, string role, string email, IReadOnlyList<TestPet> pets, CancellationToken cancellationToken)
    {
        var created = 0;

        foreach (var pet in pets)
        {
            var result = await registerPet
                .HandleAsync(
                    new RegisterPetCommand(
                        userId,
                        role,
                        pet.Species,
                        SpeciesDescription: null,
                        pet.Name,
                        pet.Breed,
                        pet.Size,
                        pet.BirthDate,
                        pet.Sex,
                        pet.IsNeutered,
                        pet.IsVaccinated,
                        pet.MedicationNotes,
                        pet.FeedingNotes,
                        pet.GoodWithDogs,
                        pet.GoodWithCats,
                        pet.GoodWithKids,
                        VetContact: null,
                        pet.Notes,
                        pet.WeightKg),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
                created++;
            else
                LogPetRejected(logger, pet.Name, email, Describe(result));
        }

        return created;
    }

    private static ListingSeed ToListingSeed(TestListing listing, AddressData address) =>
        new(
            new ListingDetailsData(
                listing.Title,
                listing.Description,
                listing.HomeType,
                listing.HasYard,
                listing.HasDogs,
                listing.HasCats,
                listing.HasKids,
                [.. listing.AcceptedSpecies],
                listing.AcceptsSmall,
                listing.AcceptsMedium,
                listing.AcceptsLarge,
                listing.Capacity,
                listing.NightlyRate,
                listing.HouseRules,
                [.. listing.AvailableWeekdays]),
            new ListingAddressData(address.ZipCode, address.Street, address.Number, address.Complement, address.Neighborhood, address.City, address.State),
            listing.Latitude,
            listing.Longitude);

    private static string Describe(Result result) =>
        string.Join(", ", (result.Errors ?? []).Select(e => e.Field is null ? e.Code : $"{e.Field}:{e.Code}"));

    [LoggerMessage(EventId = 9000, Level = LogLevel.Information, Message = "Test data: {Role} {Email} already exists; skipped.")]
    private static partial void LogAlreadyExists(ILogger logger, string role, string email);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Warning, Message = "Test data: {Role} {Email} was rejected: {Errors}.")]
    private static partial void LogRejected(ILogger logger, string role, string email, string errors);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Information, Message = "Test data: owner {Email} created with {Pets} pet(s).")]
    private static partial void LogOwnerCreated(ILogger logger, string email, int pets);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Information, Message = "Test data: host {Email} created (listing: {Listing}, pets: {Pets}).")]
    private static partial void LogHostCreated(ILogger logger, string email, bool listing, int pets);

    [LoggerMessage(EventId = 9004, Level = LogLevel.Warning, Message = "Test data: listing of {Email} was rejected: {Errors}.")]
    private static partial void LogListingRejected(ILogger logger, string email, string errors);

    [LoggerMessage(EventId = 9005, Level = LogLevel.Warning, Message = "Test data: pet {Name} of {Email} was rejected: {Errors}.")]
    private static partial void LogPetRejected(ILogger logger, string name, string email, string errors);

    [LoggerMessage(EventId = 9006, Level = LogLevel.Warning, Message = "Test data seed is enabled but Password is missing; skipped.")]
    internal static partial void LogIncomplete(ILogger logger);
}

/// <summary>Registro e passo de inicialização dos dados de teste.</summary>
public static class TestDataSeedExtensions
{
    public static IServiceCollection AddTestDataSeed(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TestDataSeedOptions>().Bind(configuration.GetSection(TestDataSeedOptions.SectionName));
        services.AddScoped<TestDataSeeder>();

        return services;
    }

    /// <summary>
    /// Roda o <see cref="TestDataSeeder"/> se <c>Seed:TestData:Enabled</c>. Depois da inicialização
    /// de todos os módulos: precisa das tabelas do Auth, Owners, Hosts, Pets e Listings.
    /// </summary>
    public static async Task SeedTestDataAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();

        var settings = scope.ServiceProvider.GetRequiredService<IOptions<TestDataSeedOptions>>().Value;
        if (!settings.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(settings.Password))
        {
            TestDataSeeder.LogIncomplete(scope.ServiceProvider.GetRequiredService<ILogger<TestDataSeeder>>());
            return;
        }

        await scope.ServiceProvider
            .GetRequiredService<TestDataSeeder>()
            .SeedAsync(settings.Password, cancellationToken)
            .ConfigureAwait(false);
    }
}
