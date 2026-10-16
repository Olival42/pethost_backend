using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Conversations.GetConversationById;
using PetHost.Modules.Conversations.Application.Conversations.GetUnreadMessageCount;
using PetHost.Modules.Conversations.Application.Conversations.ListMyConversations;
using PetHost.Modules.Conversations.Application.Conversations.MarkConversationRead;
using PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Conversations.StartConversation;
using PetHost.Modules.Conversations.Application.Messages.ListConversationMessages;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Application.Messages.SendMessage;
using PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Infrastructure.Bookings;
using PetHost.Modules.Conversations.Infrastructure.Listings;
using PetHost.Modules.Conversations.Infrastructure.Parties;
using PetHost.Modules.Conversations.Infrastructure.Persistence;
using PetHost.Modules.Conversations.Infrastructure.Persistence.Queries;
using PetHost.Modules.Conversations.Infrastructure.Persistence.Repositories;
using PetHost.Modules.Conversations.Infrastructure.Retention;
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
        services.AddScoped<IConversationsUnitOfWork, ConversationsUnitOfWork>();
        services.AddScoped<IConversationQueries, ConversationQueries>();
        services.AddScoped<IConversationParties, ConversationParties>();
        services.AddScoped<IConversationListings, ConversationListings>();
        services.AddScoped<IConversationBookings, ConversationBookings>();
        services.AddScoped<ConversationResponseBuilder>();
        services.AddScoped<ConversationNotifications>();

        services.AddValidatedCommandHandler<StartConversationCommandHandler, StartConversationCommand, StartedConversation>();
        services.AddValidatedCommandHandler<SendMessageCommandHandler, SendMessageCommand, MessageResponse>();
        services.AddValidatedCommandHandler<SendPhotoMessageCommandHandler, SendPhotoMessageCommand, MessageResponse>();
        services.AddValidatedCommandHandler<MarkConversationReadCommandHandler, MarkConversationReadCommand, UnreadCountResponse>();
        services.AddValidatedCommandHandler<PurgeInactiveConversationsCommandHandler, PurgeInactiveConversationsCommand, PurgeInactiveConversationsResponse>();

        services.AddScoped<IQueryHandler<ListMyConversationsQuery, PagedResult<ConversationResponse>>, ListMyConversationsQueryHandler>();
        services.AddScoped<IQueryHandler<GetConversationByIdQuery, ConversationResponse>, GetConversationByIdQueryHandler>();
        services.AddScoped<IQueryHandler<GetUnreadMessageCountQuery, UnreadCountResponse>, GetUnreadMessageCountQueryHandler>();
        services.AddScoped<IQueryHandler<ListConversationMessagesQuery, MessagePageResponse>, ListConversationMessagesQueryHandler>();

        services.AddOptions<ConversationRetentionOptions>()
            .Bind(configuration.GetSection(ConversationRetentionOptions.SectionName))
            .Validate(o => o.IntervalMinutes > 0, "ConversationRetention:IntervalMinutes must be greater than zero.")
            .Validate(o => o.InactivityDays > 0, "ConversationRetention:InactivityDays must be greater than zero.")
            .Validate(
                o => o.BatchSize > 0 && o.BatchSize <= ConversationsErrors.MaxRetentionBatchSize,
                $"ConversationRetention:BatchSize must be between 1 and {ConversationsErrors.MaxRetentionBatchSize}.")
            .ValidateOnStart();

        services.AddHostedService<ConversationRetentionService>();

        return services;
    }
}
