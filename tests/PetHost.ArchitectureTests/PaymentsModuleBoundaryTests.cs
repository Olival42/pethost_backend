using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Payments.Application.Payments.ChoosePaymentPlan;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Modules.Payments.Presentation.Payments;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Regras do §4 para o módulo Payments (pagamentos) e as fronteiras do §5: o Payments acha a
/// reserva, o tutor e o anfitrião só pelos contratos de <c>Shared.Contracts</c>, e ninguém
/// referencia o Payments — o Bookings conversa com ele por <c>IPaymentDirectory</c> e <c>IBookingPaymentsSync</c>.
/// </summary>
public sealed class PaymentsModuleBoundaryTests
{
    private static readonly Assembly PaymentsDomain = typeof(Payment).Assembly;
    private static readonly Assembly PaymentsApplication = typeof(ChoosePaymentPlanCommand).Assembly;
    private static readonly Assembly PaymentsInfrastructure = typeof(Modules.Payments.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly PaymentsPresentation = typeof(BookingPaymentController).Assembly;

    private static readonly Assembly[] PaymentsAssemblies = [PaymentsDomain, PaymentsApplication, PaymentsInfrastructure, PaymentsPresentation];

    private static readonly Assembly[] OtherModules =
    [
        typeof(Modules.Auth.Domain.Users.User).Assembly,
        typeof(Modules.Auth.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Owners.Domain.Owners.Owner).Assembly,
        typeof(Modules.Owners.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Hosts.Domain.Hosts.Host).Assembly,
        typeof(Modules.Hosts.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Pets.Domain.Pets.Pet).Assembly,
        typeof(Modules.Pets.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Listings.Domain.Listings.Listing).Assembly,
        typeof(Modules.Listings.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Bookings.Domain.Bookings.Booking).Assembly,
        typeof(Modules.Bookings.Application.Bookings.CreateBooking.CreateBookingCommand).Assembly,
        typeof(Modules.Bookings.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Bookings.Presentation.Bookings.BookingsController).Assembly,
        typeof(Modules.Audit.Domain.Entries.AuditEntry).Assembly,
        typeof(Modules.Audit.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Conversations.Domain.Conversations.Conversation).Assembly,
        typeof(Modules.Conversations.Application.Conversations.StartConversation.StartConversationCommand).Assembly,
        typeof(Modules.Conversations.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Conversations.Presentation.Conversations.ConversationsController).Assembly,
    ];

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";
    private const string Stripe = "Stripe";

    [Fact]
    public void PaymentsModule_Should_NotReferenceOtherModules()
    {
        foreach (var assembly in PaymentsAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Auth", "PetHost.Modules.Owners", "PetHost.Modules.Hosts", "PetHost.Modules.Pets", "PetHost.Modules.Listings", "PetHost.Modules.Bookings", "PetHost.Modules.Audit", "PetHost.Modules.Conversations")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} só fala com outro módulo por Shared.Contracts (§5)");
        }
    }

    [Fact]
    public void OtherModules_Should_NotReferencePayments()
    {
        foreach (var assembly in OtherModules)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("PetHost.Modules.Payments")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar o Payments (§5)");
        }
    }

    [Fact]
    public void PaymentsDomain_Should_NotDependOnFrameworksStripeOrOuterLayers()
    {
        Types.InAssembly(PaymentsDomain)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                Stripe,
                "PetHost.Modules.Payments.Application",
                "PetHost.Modules.Payments.Infrastructure",
                "PetHost.Modules.Payments.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PaymentsApplication_Should_NotDependOnInfrastructureStripeOrPresentation()
    {
        Types.InAssembly(PaymentsApplication)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                Stripe,
                "System.Net.Http",
                "PetHost.Modules.Payments.Infrastructure",
                "PetHost.Modules.Payments.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PaymentsPresentation_Should_NotDependOnDomainOrInfrastructure()
    {
        Types.InAssembly(PaymentsPresentation)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Modules.Payments.Domain", "PetHost.Modules.Payments.Infrastructure", Stripe)
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PaymentsInfrastructure_Should_NotDependOnPresentation()
    {
        Types.InAssembly(PaymentsInfrastructure)
            .Should()
            .NotHaveDependencyOn("PetHost.Modules.Payments.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PaymentsHandlersAndValidators_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(PaymentsApplication)
            .That().HaveNameEndingWith("Handler").Or().HaveNameEndingWith("Validator").Or().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
