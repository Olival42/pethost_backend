using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.DeactivatePet;
using PetHost.Modules.Pets.Application.Pets.GetPetById;
using PetHost.Modules.Pets.Application.Pets.ListHostPets;
using PetHost.Modules.Pets.Application.Pets.ListMyPets;
using PetHost.Modules.Pets.Application.Pets.ReactivatePet;
using PetHost.Modules.Pets.Application.Pets.RegisterPet;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Application.Pets.UpdatePet;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Modules.Pets.Infrastructure.Keepers;
using PetHost.Modules.Pets.Infrastructure.Persistence;
using PetHost.Modules.Pets.Infrastructure.Persistence.Repositories;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Pets (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Mesma connection string dos outros módulos: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddPetsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<PetsDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(PetsDbContext.MigrationsHistoryTable, PetsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IPetRepository, PetRepository>();
        services.AddScoped<IPetsUnitOfWork, PetsUnitOfWork>();
        services.AddScoped<IPetKeepers, PetKeepers>();

        services.AddValidatedCommandHandler<RegisterPetCommandHandler, RegisterPetCommand, PetResponse>();
        services.AddValidatedCommandHandler<UpdatePetCommandHandler, UpdatePetCommand, PetResponse>();
        services.AddValidatedCommandHandler<DeactivatePetCommandHandler, DeactivatePetCommand, PetResponse>();
        services.AddValidatedCommandHandler<ReactivatePetCommandHandler, ReactivatePetCommand, PetResponse>();

        services.AddScoped<IQueryHandler<GetPetByIdQuery, PetResponse>, GetPetByIdQueryHandler>();
        services.AddScoped<IQueryHandler<ListMyPetsQuery, KeeperPetsResponse>, ListMyPetsQueryHandler>();
        services.AddScoped<IQueryHandler<ListHostPetsQuery, KeeperPetsResponse>, ListHostPetsQueryHandler>();

        return services;
    }
}
