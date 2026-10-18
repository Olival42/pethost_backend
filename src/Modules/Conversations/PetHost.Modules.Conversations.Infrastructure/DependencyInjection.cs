using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Conversations.BlockConversation;
using PetHost.Modules.Conversations.Application.Conversations.EraseConversationsOfParty;
using PetHost.Modules.Conversations.Application.Conversations.LiftModerationBlock;
using PetHost.Modules.Conversations.Application.Conversations.GetConversationById;
using PetHost.Modules.Conversations.Application.Conversations.GetUnreadMessageCount;
using PetHost.Modules.Conversations.Application.Conversations.ListMyConversations;
using PetHost.Modules.Conversations.Application.Conversations.MarkConversationRead;
using PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Conversations.StartConversation;
using PetHost.Modules.Conversations.Application.Conversations.UnblockConversation;
using PetHost.Modules.Conversations.Application.Messages.ListConversationMessages;
using PetHost.Modules.Conversations.Application.Messages.NotifyUnreadMessages;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Application.Messages.SendMessage;
using PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;
using PetHost.Modules.Conversations.Application.Preferences;
using PetHost.Modules.Conversations.Application.Preferences.GetNotificationSettings;
using PetHost.Modules.Conversations.Application.Preferences.UpdateNotificationSettings;
using PetHost.Modules.Conversations.Application.Reports.GetMessageReportById;
using PetHost.Modules.Conversations.Application.Reports.ListMessageReports;
using PetHost.Modules.Conversations.Application.Reports.ReportMessage;
using PetHost.Modules.Conversations.Application.Reports.ResolveMessageReport;
using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Preferences;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Modules.Conversations.Infrastructure.Bookings;
using PetHost.Modules.Conversations.Infrastructure.Listings;
using PetHost.Modules.Conversations.Infrastructure.Moderation;
using PetHost.Modules.Conversations.Infrastructure.Parties;
using PetHost.Modules.Conversations.Infrastructure.Persistence;
using PetHost.Modules.Conversations.Infrastructure.Persistence.Queries;
using PetHost.Modules.Conversations.Infrastructure.Persistence.Repositories;
using PetHost.Modules.Conversations.Infrastructure.Privacy;
using PetHost.Modules.Conversations.Infrastructure.Retention;
using PetHost.Modules.Conversations.Infrastructure.UnreadEmail;
using PetHost.Shared.Contracts.Privacy;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Conversations (§3).</summary>
/// <remarks>
/// O aviso em tempo real (<see cref="IConversationRealtime"/>) é registrado pela Presentation, que
/// conhece o hub.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>Mesma connection string dos outros módulos: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddConversationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<ConversationsDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(ConversationsDbContext.MigrationsHistoryTable, ConversationsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IMessageReportRepository, MessageReportRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<IConversationsUnitOfWork, ConversationsUnitOfWork>();
        services.AddScoped<IConversationQueries, ConversationQueries>();
        services.AddScoped<IConversationParties, ConversationParties>();
        services.AddScoped<IConversationListings, ConversationListings>();
        services.AddScoped<IConversationBookings, ConversationBookings>();

        // Exclusão da conta (LGPD): a parte do Conversations (§5).
        services.AddScoped<IPersonalDataEraser, ConversationPersonalDataEraser>();
        services.AddScoped<ConversationResponseBuilder>();
        services.AddScoped<ConversationNotifications>();
        services.AddScoped<MessageReportResponseBuilder>();
        services.AddScoped<IModerationAlerts, ModerationEmailAlerts>();
        services.AddScoped<IUnreadMessagesNotifier, UnreadMessagesEmailNotifier>();
        services.AddOptions<ConversationModerationOptions>()
            .Bind(configuration.GetSection(ConversationModerationOptions.SectionName));

        services.AddValidatedCommandHandler<StartConversationCommandHandler, StartConversationCommand, StartedConversation>();
        services.AddValidatedCommandHandler<SendMessageCommandHandler, SendMessageCommand, MessageResponse>();
        services.AddValidatedCommandHandler<SendPhotoMessageCommandHandler, SendPhotoMessageCommand, MessageResponse>();
        services.AddValidatedCommandHandler<MarkConversationReadCommandHandler, MarkConversationReadCommand, UnreadCountResponse>();
        services.AddValidatedCommandHandler<BlockConversationCommandHandler, BlockConversationCommand, ConversationResponse>();
        services.AddValidatedCommandHandler<UnblockConversationCommandHandler, UnblockConversationCommand, ConversationResponse>();
        services.AddValidatedCommandHandler<ReportMessageCommandHandler, ReportMessageCommand, SubmittedReportResponse>();
        services.AddValidatedCommandHandler<LiftModerationBlockCommandHandler, LiftModerationBlockCommand, ConversationBlockResponse>();
        services.AddValidatedCommandHandler<ResolveMessageReportCommandHandler, ResolveMessageReportCommand, MessageReportResponse>();
        services.AddValidatedCommandHandler<UpdateNotificationSettingsCommandHandler, UpdateNotificationSettingsCommand, NotificationSettingsResponse>();
        services.AddValidatedCommandHandler<NotifyUnreadMessagesCommandHandler, NotifyUnreadMessagesCommand, NotifyUnreadMessagesResponse>();
        services.AddValidatedCommandHandler<PurgeInactiveConversationsCommandHandler, PurgeInactiveConversationsCommand, PurgeInactiveConversationsResponse>();
        services.AddValidatedCommandHandler<EraseConversationsOfPartyCommandHandler, EraseConversationsOfPartyCommand, int>();

        services.AddScoped<IQueryHandler<ListMyConversationsQuery, PagedResult<ConversationResponse>>, ListMyConversationsQueryHandler>();
        services.AddScoped<IQueryHandler<GetConversationByIdQuery, ConversationResponse>, GetConversationByIdQueryHandler>();
        services.AddScoped<IQueryHandler<GetUnreadMessageCountQuery, UnreadCountResponse>, GetUnreadMessageCountQueryHandler>();
        services.AddScoped<IQueryHandler<ListConversationMessagesQuery, MessagePageResponse>, ListConversationMessagesQueryHandler>();
        services.AddScoped<IQueryHandler<GetNotificationSettingsQuery, NotificationSettingsResponse>, GetNotificationSettingsQueryHandler>();
        services.AddScoped<IQueryHandler<ListMessageReportsQuery, PagedResult<MessageReportResponse>>, ListMessageReportsQueryHandler>();
        services.AddScoped<IQueryHandler<GetMessageReportByIdQuery, MessageReportResponse>, GetMessageReportByIdQueryHandler>();

        services.AddOptions<ConversationRetentionOptions>()
            .Bind(configuration.GetSection(ConversationRetentionOptions.SectionName))
            .Validate(o => o.IntervalMinutes > 0, "ConversationRetention:IntervalMinutes must be greater than zero.")
            .Validate(o => o.InactivityDays > 0, "ConversationRetention:InactivityDays must be greater than zero.")
            .Validate(
                o => o.BatchSize > 0 && o.BatchSize <= ConversationsErrors.MaxRetentionBatchSize,
                $"ConversationRetention:BatchSize must be between 1 and {ConversationsErrors.MaxRetentionBatchSize}.")
            .ValidateOnStart();

        services.AddHostedService<ConversationRetentionService>();

        services.AddOptions<UnreadEmailOptions>()
            .Bind(configuration.GetSection(UnreadEmailOptions.SectionName))
            .Validate(o => o.IntervalMinutes > 0, "ConversationUnreadEmail:IntervalMinutes must be greater than zero.")
            .Validate(o => o.DelayMinutes > 0, "ConversationUnreadEmail:DelayMinutes must be greater than zero.")
            .Validate(o => o.CooldownMinutes > 0, "ConversationUnreadEmail:CooldownMinutes must be greater than zero.")
            .Validate(
                o => o.BatchSize > 0 && o.BatchSize <= ConversationsErrors.MaxRetentionBatchSize,
                $"ConversationUnreadEmail:BatchSize must be between 1 and {ConversationsErrors.MaxRetentionBatchSize}.")
            .ValidateOnStart();

        services.AddHostedService<UnreadEmailService>();

        return services;
    }
}
