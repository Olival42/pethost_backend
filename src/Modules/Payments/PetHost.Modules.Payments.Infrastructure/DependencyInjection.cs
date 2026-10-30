using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments;
using PetHost.Modules.Payments.Application.Payments.ChoosePaymentPlan;
using PetHost.Modules.Payments.Application.Payments.GetBookingPayment;
using PetHost.Modules.Payments.Application.Payments.ListMyPayouts;
using PetHost.Modules.Payments.Application.Payments.ListPaymentIssues;
using PetHost.Modules.Payments.Application.Payments.ReconcileBookingPayment;
using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Application.Payments.RunPaymentsMaintenance;
using PetHost.Modules.Payments.Application.Payments.StartInstallmentPayment;
using PetHost.Modules.Payments.Application.Payments.SyncBookingPayment;
using PetHost.Modules.Payments.Application.Payments.SyncPaymentIntent;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Modules.Payments.Infrastructure.Adapters;
using PetHost.Modules.Payments.Infrastructure.Contracts;
using PetHost.Modules.Payments.Infrastructure.Maintenance;
using PetHost.Modules.Payments.Infrastructure.Notifications;
using PetHost.Modules.Payments.Infrastructure.Persistence;
using PetHost.Modules.Payments.Infrastructure.Persistence.Repositories;
using PetHost.Modules.Payments.Infrastructure.Stripe;
using PetHost.Shared.Contracts.Payments;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Stripe;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Payments.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Payments (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Mesma connection string dos outros módulos: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddPaymentsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<PaymentsDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(PaymentsDbContext.MigrationsHistoryTable, PaymentsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentsUnitOfWork, PaymentsUnitOfWork>();
        services.AddScoped<IPaymentBookings, PaymentBookings>();
        services.AddScoped<IPaymentParties, PaymentParties>();
        services.AddScoped<IPaymentNotifier, PaymentEmailNotifier>();
        services.AddScoped<PaymentReconciler>();
        services.AddScoped<PaymentEffectsPublisher>();
        services.AddScoped<PaymentCommitter>();

        // Stripe (modo de teste): cobranças, reembolsos e repasses, e os webhooks das cobranças
        // no endpoint único (§5).
        services.AddStripe(configuration);
        services.AddScoped<IStripePayments, StripePaymentsGateway>();
        services.AddScoped<IStripeWebhookHandler, PaymentWebhookHandler>();

        // Contratos públicos: o Bookings pergunta se o tutor está devendo e avisa quando a reserva muda (§5).
        services.AddScoped<IPaymentDirectory, PaymentDirectory>();
        services.AddScoped<IBookingPaymentsSync, BookingPaymentsSync>();

        services.AddValidatedCommandHandler<ChoosePaymentPlanCommandHandler, ChoosePaymentPlanCommand, PaymentResponse>();
        services.AddValidatedCommandHandler<StartInstallmentPaymentCommandHandler, StartInstallmentPaymentCommand, PaymentSessionResponse>();
        services.AddValidatedCommandHandler<SyncBookingPaymentCommandHandler, SyncBookingPaymentCommand, PaymentResponse>();
        services.AddValidatedCommandHandler<SyncPaymentIntentCommandHandler, SyncPaymentIntentCommand, SyncPaymentIntentResponse>();
        services.AddValidatedCommandHandler<ReconcileBookingPaymentCommandHandler, ReconcileBookingPaymentCommand, Unit>();
        services.AddValidatedCommandHandler<RunPaymentsMaintenanceCommandHandler, RunPaymentsMaintenanceCommand, RunPaymentsMaintenanceResponse>();

        services.AddScoped<IQueryHandler<GetBookingPaymentQuery, PaymentResponse>, GetBookingPaymentQueryHandler>();
        services.AddScoped<IQueryHandler<ListMyPayoutsQuery, PagedResult<HostEarningsResponse>>, ListMyPayoutsQueryHandler>();
        services.AddScoped<IQueryHandler<ListPaymentIssuesQuery, IReadOnlyList<PaymentIssueResponse>>, ListPaymentIssuesQueryHandler>();

        services.AddOptions<PaymentsOptions>()
            .Bind(configuration.GetSection(PaymentsOptions.SectionName))
            .Validate(o => o.MaintenanceIntervalMinutes > 0, "Payments:MaintenanceIntervalMinutes must be greater than zero.")
            .ValidateOnStart();

        services.AddHostedService<PaymentsMaintenanceService>();

        return services;
    }
}
