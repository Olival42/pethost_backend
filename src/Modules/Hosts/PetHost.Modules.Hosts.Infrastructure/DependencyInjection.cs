using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.ApproveHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.CancelMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.GetHostApplicationById;
using PetHost.Modules.Hosts.Application.HostApplications.GetHostApplicationDocument;
using PetHost.Modules.Hosts.Application.HostApplications.GetMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.ListHostApplications;
using PetHost.Modules.Hosts.Application.HostApplications.PurgeHostApplications;
using PetHost.Modules.Hosts.Application.HostApplications.RejectHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Application.HostApplications.StartHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.SubmitMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.UpdateMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.UploadHostApplicationDocuments;
using PetHost.Modules.Hosts.Application.Hosts.ChangeMyHostAvatar;
using PetHost.Modules.Hosts.Application.Hosts.DeactivateMyHost;
using PetHost.Modules.Hosts.Application.Hosts.GetHostById;
using PetHost.Modules.Hosts.Application.Hosts.GetMyHost;
using PetHost.Modules.Hosts.Application.Hosts.ReactivateHost;
using PetHost.Modules.Hosts.Application.Hosts.SuspendHost;
using PetHost.Modules.Hosts.Application.Hosts.UpdateMyHost;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Accounts;
using PetHost.Modules.Hosts.Infrastructure.Contracts;
using PetHost.Modules.Hosts.Infrastructure.Notifications;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Hosts.Infrastructure.Persistence.Repositories;
using PetHost.Modules.Hosts.Infrastructure.Persistence.Seed;
using PetHost.Modules.Hosts.Infrastructure.Verification;
using PetHost.Shared.Contracts.Hosts;
using PetHost.Shared.Contracts.Identity;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Hosts.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Hosts (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Mesma connection string dos outros módulos: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddHostsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<HostsDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(HostsDbContext.MigrationsHistoryTable, HostsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IHostRepository, HostRepository>();
        services.AddScoped<IHostApplicationRepository, HostApplicationRepository>();
        services.AddScoped<IHostsUnitOfWork, HostsUnitOfWork>();
        services.AddScoped<IHostAccounts, HostAccounts>();
        services.AddScoped<IHostApplicationNotifier, HostApplicationEmailNotifier>();

        // Prazos da verificação + tarefa de limpeza em segundo plano.
        services.AddOptions<HostVerificationOptions>()
            .Bind(configuration.GetSection(HostVerificationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<HostVerificationOptions>, HostVerificationOptionsValidator>();
        services.AddSingleton<IHostVerificationPolicy, HostVerificationPolicy>();
        services.AddHostedService<HostApplicationsPurgeService>();

        // Anfitrião de demonstração (desligado por padrão).
        services.AddOptions<DemoHostSeedOptions>().Bind(configuration.GetSection(DemoHostSeedOptions.SectionName));
        services.AddScoped<HostsDbSeeder>();

        // Contrato público de leitura: outros módulos acham o anfitrião só por aqui (§5).
        services.AddScoped<IHostDirectory, HostDirectory>();

        // Regra do CPF entre papéis: o Hosts informa quais contas usam um CPF.
        services.AddScoped<ICpfHolderLookup, HostCpfHolderLookup>();

        // Solicitação de cadastro: a própria pessoa.
        services.AddValidatedCommandHandler<
            StartHostApplicationCommandHandler, StartHostApplicationCommand, HostApplicationResponse>();

        services.AddValidatedCommandHandler<
            UpdateMyHostApplicationCommandHandler, UpdateMyHostApplicationCommand, HostApplicationResponse>();

        services.AddValidatedCommandHandler<
            UploadHostApplicationDocumentsCommandHandler, UploadHostApplicationDocumentsCommand, HostApplicationResponse>();

        services.AddValidatedCommandHandler<
            SubmitMyHostApplicationCommandHandler, SubmitMyHostApplicationCommand, HostApplicationResponse>();

        services.AddValidatedCommandHandler<
            CancelMyHostApplicationCommandHandler, CancelMyHostApplicationCommand, HostApplicationResponse>();

        // Solicitação de cadastro: o admin.
        services.AddValidatedCommandHandler<
            ApproveHostApplicationCommandHandler, ApproveHostApplicationCommand, HostApplicationReviewResponse>();

        services.AddValidatedCommandHandler<
            RejectHostApplicationCommandHandler, RejectHostApplicationCommand, HostApplicationReviewResponse>();

        // Tarefa de limpeza.
        services.AddValidatedCommandHandler<
            PurgeHostApplicationsCommandHandler, PurgeHostApplicationsCommand, PurgeHostApplicationsResponse>();

        // Anfitrião verificado: o próprio.
        services.AddValidatedCommandHandler<UpdateMyHostCommandHandler, UpdateMyHostCommand, HostResponse>();
        services.AddValidatedCommandHandler<ChangeMyHostAvatarCommandHandler, ChangeMyHostAvatarCommand, HostResponse>();
        services.AddValidatedCommandHandler<RemoveMyHostAvatarCommandHandler, RemoveMyHostAvatarCommand, HostResponse>();
        services.AddValidatedCommandHandler<DeactivateMyHostCommandHandler, DeactivateMyHostCommand, Unit>();
        services.AddValidatedCommandHandler<ReactivateHostCommandHandler, ReactivateHostCommand, HostSessionResponse>();

        // Anfitrião verificado: o admin.
        services.AddValidatedCommandHandler<SuspendHostCommandHandler, SuspendHostCommand, HostResponse>();
        services.AddValidatedCommandHandler<LiftHostSuspensionCommandHandler, LiftHostSuspensionCommand, HostResponse>();
        services.AddValidatedCommandHandler<ReleaseHostIdentityCommandHandler, ReleaseHostIdentityCommand, HostResponse>();
        services.AddScoped<IQueryHandler<GetHostByIdQuery, HostResponse>, GetHostByIdQueryHandler>();
        services.AddScoped<IQueryHandler<ListHostsQuery, IReadOnlyList<HostResponse>>, ListHostsQueryHandler>();

        services.AddScoped<IQueryHandler<GetMyHostApplicationQuery, MyHostApplicationResponse>, GetMyHostApplicationQueryHandler>();
        services.AddScoped<
            IQueryHandler<ListHostApplicationsQuery, IReadOnlyList<HostApplicationReviewResponse>>,
            ListHostApplicationsQueryHandler>();
        services.AddScoped<IQueryHandler<GetHostApplicationByIdQuery, HostApplicationReviewResponse>, GetHostApplicationByIdQueryHandler>();
        services.AddScoped<IQueryHandler<GetHostApplicationDocumentQuery, HostApplicationDocumentFile>, GetHostApplicationDocumentQueryHandler>();
        services.AddScoped<IQueryHandler<GetMyHostQuery, HostResponse>, GetMyHostQueryHandler>();

        return services;
    }
}
