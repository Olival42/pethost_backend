using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.AddListingPhoto;
using PetHost.Modules.Listings.Application.Listings.CreateListing;
using PetHost.Modules.Listings.Application.Listings.DeactivateListing;
using PetHost.Modules.Listings.Application.Listings.GetListingById;
using PetHost.Modules.Listings.Application.Listings.ListMyListings;
using PetHost.Modules.Listings.Application.Listings.ReactivateListing;
using PetHost.Modules.Listings.Application.Listings.RemoveListingPhoto;
using PetHost.Modules.Listings.Application.Listings.ReorderListingPhotos;
using PetHost.Modules.Listings.Application.Listings.ReplaceListingPhoto;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Application.Listings.SearchListings;
using PetHost.Modules.Listings.Application.Listings.UpdateListing;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Infrastructure.Accounts;
using PetHost.Modules.Listings.Infrastructure.Geocoding;
using PetHost.Modules.Listings.Infrastructure.Hosts;
using PetHost.Modules.Listings.Infrastructure.Persistence;
using PetHost.Modules.Listings.Infrastructure.Persistence.Repositories;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Redis;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Messaging;
using StackExchange.Redis;

namespace PetHost.Modules.Listings.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Listings (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Mesma connection string dos outros módulos: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddListingsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<ListingsDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(ListingsDbContext.MigrationsHistoryTable, ListingsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IListingRepository, ListingRepository>();
        services.AddScoped<IListingsUnitOfWork, ListingsUnitOfWork>();
        services.AddScoped<IListingHosts, ListingHosts>();
        services.AddScoped<IAccountAddresses, AccountAddresses>();

        AddGeocoding(services, configuration);

        services.AddValidatedCommandHandler<CreateListingCommandHandler, CreateListingCommand, ListingResponse>();
        services.AddValidatedCommandHandler<UpdateListingCommandHandler, UpdateListingCommand, ListingResponse>();
        services.AddValidatedCommandHandler<DeactivateListingCommandHandler, DeactivateListingCommand, ListingResponse>();
        services.AddValidatedCommandHandler<ReactivateListingCommandHandler, ReactivateListingCommand, ListingResponse>();
        services.AddValidatedCommandHandler<AddListingPhotoCommandHandler, AddListingPhotoCommand, ListingResponse>();
        services.AddValidatedCommandHandler<ReplaceListingPhotoCommandHandler, ReplaceListingPhotoCommand, ListingResponse>();
        services.AddValidatedCommandHandler<RemoveListingPhotoCommandHandler, RemoveListingPhotoCommand, ListingResponse>();
        services.AddValidatedCommandHandler<ReorderListingPhotosCommandHandler, ReorderListingPhotosCommand, ListingResponse>();

        services.AddScoped<IQueryHandler<GetListingByIdQuery, ListingResponse>, GetListingByIdQueryHandler>();
        services.AddScoped<IQueryHandler<ListMyListingsQuery, MyListingsResponse>, ListMyListingsQueryHandler>();
        services.AddScoped<IQueryHandler<SearchListingsQuery, PagedResult<ListingSummaryResponse>>, SearchListingsQueryHandler>();

        return services;
    }

    /// <summary>Nominatim com fila de 1 pedido/s e cache no Redis na frente (vale para todas as instâncias).</summary>
    private static void AddGeocoding(IServiceCollection services, IConfiguration configuration)
    {
        services.AddRedis(configuration);

        services.AddOptions<GeocodingOptions>()
            .Bind(configuration.GetSection(GeocodingOptions.SectionName))
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "Geocoding:BaseUrl must be an absolute URL.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.UserAgent), "Geocoding:UserAgent is required by the Nominatim usage policy.")
            .Validate(o => o.TimeoutSeconds > 0 && o.CacheHours > 0 && o.NotFoundCacheMinutes > 0 && o.MinIntervalMilliseconds >= 0, "Geocoding timings must be positive.")
            .ValidateOnStart();

        services.AddSingleton<GeocodingRateGate>();

        services.AddHttpClient<NominatimGeocoder>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<GeocodingOptions>>().Value;

            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR");
        });

        services.AddScoped<IGeocoder>(provider => new CachedGeocoder(
            provider.GetRequiredService<NominatimGeocoder>(),
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<IOptions<GeocodingOptions>>(),
            provider.GetRequiredService<ILogger<CachedGeocoder>>()));
    }
}
