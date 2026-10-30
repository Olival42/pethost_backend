using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Conversations.Application.Conversations.StartConversation;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Presentation.Conversations;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Regras do §4 para o módulo Conversations (chat) e as fronteiras do §5: o Conversations acha
/// tutor, anfitrião, cantinho e reserva só pelos contratos de <c>Shared.Contracts</c>, e ninguém
/// referencia o Conversations. O hub fica na Presentation; a Application só conhece a porta do
/// aviso em tempo real.
/// </summary>
public sealed class ConversationsModuleBoundaryTests
{
    private static readonly Assembly ConversationsDomain = typeof(Conversation).Assembly;
    private static readonly Assembly ConversationsApplication = typeof(StartConversationCommand).Assembly;
    private static readonly Assembly ConversationsInfrastructure = typeof(Modules.Conversations.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly ConversationsPresentation = typeof(ConversationsController).Assembly;

    private static readonly Assembly[] ConversationsAssemblies =
        [ConversationsDomain, ConversationsApplication, ConversationsInfrastructure, ConversationsPresentation];

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
        typeof(Modules.Payments.Domain.Payments.Payment).Assembly,
        typeof(Modules.Payments.Application.Payments.ChoosePaymentPlan.ChoosePaymentPlanCommand).Assembly,
        typeof(Modules.Payments.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Payments.Presentation.Payments.BookingPaymentController).Assembly,
    ];

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";

    [Fact]
    public void ConversationsModule_Should_NotReferenceOtherModules()
    {
        foreach (var assembly in ConversationsAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Auth", "PetHost.Modules.Owners", "PetHost.Modules.Hosts", "PetHost.Modules.Pets", "PetHost.Modules.Listings", "PetHost.Modules.Bookings", "PetHost.Modules.Audit", "PetHost.Modules.Payments")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} só fala com outro módulo por Shared.Contracts (§5)");
        }
    }

    [Fact]
    public void OtherModules_Should_NotReferenceConversations()
    {
        foreach (var assembly in OtherModules)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("PetHost.Modules.Conversations")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar o Conversations (§5)");
        }
    }

    [Fact]
    public void ConversationsDomain_Should_NotDependOnFrameworksOrOuterLayers()
    {
        Types.InAssembly(ConversationsDomain)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Conversations.Application",
                "PetHost.Modules.Conversations.Infrastructure",
                "PetHost.Modules.Conversations.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ConversationsApplication_Should_NotDependOnInfrastructureOrPresentation()
    {
        Types.InAssembly(ConversationsApplication)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "System.Net.Http",
                "PetHost.Modules.Conversations.Infrastructure",
                "PetHost.Modules.Conversations.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue("a Application só conhece a porta do tempo real, nunca o SignalR");
    }

    [Fact]
    public void ConversationsPresentation_Should_NotDependOnDomainOrInfrastructure()
    {
        Types.InAssembly(ConversationsPresentation)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Modules.Conversations.Domain", "PetHost.Modules.Conversations.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ConversationsInfrastructure_Should_NotDependOnPresentation()
    {
        Types.InAssembly(ConversationsInfrastructure)
            .Should()
            .NotHaveDependencyOn("PetHost.Modules.Conversations.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue("o hub e o aviso em tempo real ficam na Presentation");
    }

    [Fact]
    public void ConversationsHandlersAndValidators_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(ConversationsApplication)
            .That().HaveNameEndingWith("Handler").Or().HaveNameEndingWith("Validator").Or().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
