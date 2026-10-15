using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings;
using PetHost.Modules.Bookings.Application.Bookings.AcceptBooking;
using PetHost.Modules.Bookings.Application.Bookings.CancelBooking;
using PetHost.Modules.Bookings.Application.Bookings.CancelBookingsForSuspension;
using PetHost.Modules.Bookings.Application.Bookings.CancelUnpaidBooking;
using PetHost.Modules.Bookings.Application.Bookings.CloseOverdueStays;
using PetHost.Modules.Bookings.Application.Bookings.RemindUpcomingStays;
using PetHost.Modules.Bookings.Application.Bookings.CheckInBooking;
using PetHost.Modules.Bookings.Application.Bookings.CheckOutBooking;
using PetHost.Modules.Bookings.Application.Bookings.ConfirmBookingPayment;
using PetHost.Modules.Bookings.Application.Bookings.CreateBooking;
using PetHost.Modules.Bookings.Application.Bookings.DeclineBooking;
using PetHost.Modules.Bookings.Application.Bookings.ExpireOverdueBookings;
using PetHost.Modules.Bookings.Application.Bookings.GetBookingById;
using PetHost.Modules.Bookings.Application.Bookings.ListMyBookings;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Infrastructure.Adapters;
using PetHost.Modules.Bookings.Infrastructure.Directory;
using PetHost.Modules.Bookings.Infrastructure.Expiration;
using PetHost.Modules.Bookings.Infrastructure.Notifications;
using PetHost.Modules.Bookings.Infrastructure.Payments;
using PetHost.Modules.Bookings.Infrastructure.Suspensions;
using PetHost.Modules.Bookings.Infrastructure.Persistence;
using PetHost.Modules.Bookings.Infrastructure.Persistence.Repositories;
using PetHost.Shared.Contracts.Bookings;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Bookings.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Bookings (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Mesma connection string dos outros módulos: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddBookingsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<BookingsDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(BookingsDbContext.MigrationsHistoryTable, BookingsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IBookingsUnitOfWork, BookingsUnitOfWork>();
        services.AddScoped<IBookingParties, BookingParties>();
        services.AddScoped<IBookingListings, BookingListings>();
        services.AddScoped<IBookingPets, BookingPets>();
        services.AddScoped<IBookingPayments, BookingPayments>();
        services.AddScoped<BookingResponseBuilder>();
        services.AddScoped<IBookingNotifier, BookingEmailNotifier>();
        services.AddScoped<BookingNotifications>();

        // Contratos públicos: o Pets, o Listings e o Payments consultam as reservas por aqui, e o
        // Payments confirma a reserva paga ou cancela a que ficou sem pagar (§5).
        services.AddScoped<IBookingDirectory, BookingDirectory>();
        services.AddScoped<IBookingPaymentOutcomes, BookingPaymentOutcomes>();

        // O Owners e o Hosts avisam a suspensão: as reservas que ainda não começaram são canceladas.
        services.AddScoped<IBookingSuspensions, BookingSuspensions>();

        services.AddValidatedCommandHandler<CreateBookingCommandHandler, CreateBookingCommand, BookingResponse>();
        services.AddValidatedCommandHandler<AcceptBookingCommandHandler, AcceptBookingCommand, BookingResponse>();
        services.AddValidatedCommandHandler<DeclineBookingCommandHandler, DeclineBookingCommand, BookingResponse>();
        services.AddValidatedCommandHandler<CancelBookingCommandHandler, CancelBookingCommand, BookingResponse>();
        services.AddValidatedCommandHandler<CheckInBookingCommandHandler, CheckInBookingCommand, BookingResponse>();
        services.AddValidatedCommandHandler<CheckOutBookingCommandHandler, CheckOutBookingCommand, BookingResponse>();
        services.AddValidatedCommandHandler<ExpireOverdueBookingsCommandHandler, ExpireOverdueBookingsCommand, ExpireOverdueBookingsResponse>();
        services.AddValidatedCommandHandler<CloseOverdueStaysCommandHandler, CloseOverdueStaysCommand, CloseOverdueStaysResponse>();
        services.AddValidatedCommandHandler<RemindUpcomingStaysCommandHandler, RemindUpcomingStaysCommand, int>();
        services.AddValidatedCommandHandler<ConfirmBookingPaymentCommandHandler, ConfirmBookingPaymentCommand, bool>();
        services.AddValidatedCommandHandler<CancelUnpaidBookingCommandHandler, CancelUnpaidBookingCommand, bool>();
        services.AddValidatedCommandHandler<CancelBookingsForSuspensionCommandHandler, CancelBookingsForSuspensionCommand, CancelBookingsForSuspensionResponse>();

        services.AddScoped<IQueryHandler<GetBookingByIdQuery, BookingResponse>, GetBookingByIdQueryHandler>();
        services.AddScoped<IQueryHandler<ListMyBookingsQuery, PagedResult<BookingResponse>>, ListMyBookingsQueryHandler>();

        services.AddOptions<BookingExpirationOptions>()
            .Bind(configuration.GetSection(BookingExpirationOptions.SectionName))
            .Validate(o => o.IntervalMinutes > 0, "BookingExpiration:IntervalMinutes must be greater than zero.")
            .ValidateOnStart();

        services.AddHostedService<BookingExpirationService>();

        return services;
    }
}
