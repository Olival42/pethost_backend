using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.ChangeMyAvatar;
using PetHost.Modules.Owners.Application.Owners.DeactivateMyOwner;
using PetHost.Modules.Owners.Application.Owners.GetMyOwner;
using PetHost.Modules.Owners.Application.Owners.GetOwnerById;
using PetHost.Modules.Owners.Application.Owners.ListOwners;
using PetHost.Modules.Owners.Application.Owners.ReactivateOwner;
using PetHost.Modules.Owners.Application.Owners.SuspendOwner;
using PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;
using PetHost.Modules.Owners.Application.Owners.RemoveMyAvatar;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Application.Owners.UpdateMyOwner;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Modules.Owners.Infrastructure.Accounts;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.Modules.Owners.Infrastructure.Persistence.Repositories;
using PetHost.Shared.Contracts.Owners;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Owners.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Owners (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Mesma connection string do Auth: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddOwnersInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<OwnersDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(OwnersDbContext.MigrationsHistoryTable, OwnersDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IOwnerRepository, OwnerRepository>();
        services.AddScoped<IOwnersUnitOfWork, OwnersUnitOfWork>();
        services.AddScoped<IOwnerAccounts, OwnerAccounts>();

        // Contrato público de leitura: outros módulos acham o tutor só por aqui (§5).
        services.AddScoped<IOwnerDirectory, OwnerDirectory>();

        services.AddValidatedCommandHandler<
            RegisterOwnerAccountCommandHandler, RegisterOwnerAccountCommand, OwnerSessionResponse>();

        services.AddValidatedCommandHandler<
            DeactivateMyOwnerCommandHandler, DeactivateMyOwnerCommand, Unit>();

        services.AddValidatedCommandHandler<
            ReactivateOwnerCommandHandler, ReactivateOwnerCommand, OwnerSessionResponse>();

        // Ações do admin sobre um tutor.
        services.AddValidatedCommandHandler<
            SuspendOwnerCommandHandler, SuspendOwnerCommand, OwnerResponse>();

        services.AddValidatedCommandHandler<
            LiftOwnerSuspensionCommandHandler, LiftOwnerSuspensionCommand, OwnerResponse>();

        services.AddValidatedCommandHandler<
            ReleaseOwnerCpfCommandHandler, ReleaseOwnerCpfCommand, OwnerResponse>();

        services.AddValidatedCommandHandler<
            UpdateMyOwnerCommandHandler, UpdateMyOwnerCommand, OwnerResponse>();

        services.AddValidatedCommandHandler<
            ChangeMyAvatarCommandHandler, ChangeMyAvatarCommand, OwnerResponse>();

        services.AddValidatedCommandHandler<
            RemoveMyAvatarCommandHandler, RemoveMyAvatarCommand, OwnerResponse>();

        services.AddScoped<IQueryHandler<GetMyOwnerQuery, OwnerResponse>, GetMyOwnerQueryHandler>();
        services.AddScoped<IQueryHandler<GetOwnerByIdQuery, OwnerResponse>, GetOwnerByIdQueryHandler>();
        services.AddScoped<
            IQueryHandler<ListOwnersQuery, IReadOnlyList<OwnerResponse>>,
            ListOwnersQueryHandler>();

        return services;
    }
}
