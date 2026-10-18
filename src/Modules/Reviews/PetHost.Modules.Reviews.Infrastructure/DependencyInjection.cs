using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reminders.SendReviewReminders;
using PetHost.Modules.Reviews.Application.Reports.GetReviewReportById;
using PetHost.Modules.Reviews.Application.Reports.ListReviewReports;
using PetHost.Modules.Reviews.Application.Reports.ReportReview;
using PetHost.Modules.Reviews.Application.Reports.ResolveReviewReport;
using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Modules.Reviews.Application.Reviews.CreateReview;
using PetHost.Modules.Reviews.Application.Reviews.DeleteReview;
using PetHost.Modules.Reviews.Application.Reviews.GetRatingSummary;
using PetHost.Modules.Reviews.Application.Reviews.GetReviewById;
using PetHost.Modules.Reviews.Application.Reviews.ListMyReviews;
using PetHost.Modules.Reviews.Application.Reviews.ListOwnerReviews;
using PetHost.Modules.Reviews.Application.Reviews.ListPetReviews;
using PetHost.Modules.Reviews.Application.Reviews.ListReviews;
using PetHost.Modules.Reviews.Application.Reviews.NotifyPublishedReviews;
using PetHost.Modules.Reviews.Application.Reviews.RemoveReviewReply;
using PetHost.Modules.Reviews.Application.Reviews.ReplyToReview;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Application.Reviews.UnhideReview;
using PetHost.Modules.Reviews.Application.Reviews.UpdateReview;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reminders;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Modules.Reviews.Infrastructure.Adapters;
using PetHost.Modules.Reviews.Infrastructure.Maintenance;
using PetHost.Modules.Reviews.Infrastructure.Notifications;
using PetHost.Modules.Reviews.Infrastructure.Persistence;
using PetHost.Modules.Reviews.Infrastructure.Persistence.Queries;
using PetHost.Modules.Reviews.Infrastructure.Persistence.Repositories;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Contracts.Reviews;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Reviews.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Reviews (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Mesma connection string dos outros módulos: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddReviewsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<ReviewsDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(ReviewsDbContext.MigrationsHistoryTable, ReviewsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IReviewReportRepository, ReviewReportRepository>();
        services.AddScoped<IReviewReminderRepository, ReviewReminderRepository>();
        services.AddScoped<IReviewsUnitOfWork, ReviewsUnitOfWork>();
        services.AddScoped<IReviewQueries, ReviewQueries>();
        services.AddScoped<IReviewParties, ReviewParties>();
        services.AddScoped<IReviewBookings, ReviewBookings>();
        services.AddScoped<IReviewNotifier, ReviewEmailNotifier>();
        services.AddScoped<ReviewResponseBuilder>();
        services.AddScoped<ReviewReportResponseBuilder>();

        // Contrato público: o Listings lê as notas por aqui (§5).
        services.AddScoped<IReviewDirectory, ReviewDirectory>();

        services.AddOptions<ReviewEmailOptions>().Bind(configuration.GetSection(ReviewEmailOptions.SectionName));

        services.AddValidatedCommandHandler<CreateReviewCommandHandler, CreateReviewCommand, ReviewResponse>();
        services.AddValidatedCommandHandler<UpdateReviewCommandHandler, UpdateReviewCommand, ReviewResponse>();
        services.AddValidatedCommandHandler<DeleteReviewCommandHandler, DeleteReviewCommand, Unit>();
        services.AddValidatedCommandHandler<ReplyToReviewCommandHandler, ReplyToReviewCommand, ReviewResponse>();
        services.AddValidatedCommandHandler<RemoveReviewReplyCommandHandler, RemoveReviewReplyCommand, ReviewResponse>();
        services.AddValidatedCommandHandler<ReportReviewCommandHandler, ReportReviewCommand, SubmittedReviewReportResponse>();
        services.AddValidatedCommandHandler<ResolveReviewReportCommandHandler, ResolveReviewReportCommand, ReviewReportResponse>();
        services.AddValidatedCommandHandler<UnhideReviewCommandHandler, UnhideReviewCommand, ReviewResponse>();
        services.AddValidatedCommandHandler<SendReviewRemindersCommandHandler, SendReviewRemindersCommand, SendReviewRemindersResponse>();
        services.AddValidatedCommandHandler<NotifyPublishedReviewsCommandHandler, NotifyPublishedReviewsCommand, NotifyPublishedReviewsResponse>();

        services.AddScoped<IQueryHandler<ListReviewsQuery, PagedResult<ReviewResponse>>, ListReviewsQueryHandler>();
        services.AddScoped<IQueryHandler<GetReviewByIdQuery, ReviewResponse>, GetReviewByIdQueryHandler>();
        services.AddScoped<IQueryHandler<ListMyReviewsQuery, PagedResult<ReviewResponse>>, ListMyReviewsQueryHandler>();
        services.AddScoped<IQueryHandler<ListOwnerReviewsQuery, PagedResult<ReviewResponse>>, ListOwnerReviewsQueryHandler>();
        services.AddScoped<IQueryHandler<ListPetReviewsQuery, PagedResult<PetReviewResponse>>, ListPetReviewsQueryHandler>();
        services.AddScoped<IQueryHandler<GetRatingSummaryQuery, RatingSummaryResponse>, GetRatingSummaryQueryHandler>();
        services.AddScoped<IQueryHandler<ListReviewReportsQuery, PagedResult<ReviewReportResponse>>, ListReviewReportsQueryHandler>();
        services.AddScoped<IQueryHandler<GetReviewReportByIdQuery, ReviewReportResponse>, GetReviewReportByIdQueryHandler>();

        services.AddOptions<ReviewMaintenanceOptions>()
            .Bind(configuration.GetSection(ReviewMaintenanceOptions.SectionName))
            .Validate(o => o.IntervalMinutes > 0, "ReviewMaintenance:IntervalMinutes must be greater than zero.")
            .Validate(
                o => o.ReminderDelayHours > 0 && o.ReminderDelayHours < Review.WindowDays * 24,
                $"ReviewMaintenance:ReminderDelayHours must be between 1 and {(Review.WindowDays * 24) - 1}.")
            .Validate(
                o => o.BatchSize > 0 && o.BatchSize <= ReviewsErrors.MaxBatchSize,
                $"ReviewMaintenance:BatchSize must be between 1 and {ReviewsErrors.MaxBatchSize}.")
            .ValidateOnStart();

        services.AddHostedService<ReviewMaintenanceService>();

        return services;
    }
}
