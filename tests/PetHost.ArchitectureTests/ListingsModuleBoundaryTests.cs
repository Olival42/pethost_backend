using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Listings.Application.Listings.CreateListing;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Presentation.Listings;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Regras do §4 para o módulo Listings (cantinhos) e as fronteiras do §5: o Listings acha
/// anfitrião e conta só pelos contratos de <c>Shared.Contracts</c>, e ninguém referencia o Listings.
/// </summary>
public sealed class ListingsModuleBoundaryTests
{
    private static readonly Assembly ListingsDomain = typeof(Listing).Assembly;
    private static readonly Assembly ListingsApplication = typeof(CreateListingCommand).Assembly;
    private static readonly Assembly ListingsInfrastructure = typeof(Modules.Listings.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly ListingsPresentation = typeof(ListingsController).Assembly;

    private static readonly Assembly[] ListingsAssemblies = [ListingsDomain, ListingsApplication, ListingsInfrastructure, ListingsPresentation];

    private static readonly Assembly[] OtherModules =
    [
        typeof(Modules.Auth.Domain.Users.User).Assembly,
        typeof(Modules.Auth.Application.Sessions.CreateSession.CreateSessionCommand).Assembly,
        typeof(Modules.Auth.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Auth.Presentation.Sessions.SessionsController).Assembly,
        typeof(Modules.Owners.Domain.Owners.Owner).Assembly,
        typeof(Modules.Owners.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Hosts.Domain.Hosts.Host).Assembly,
        typeof(Modules.Hosts.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Hosts.Presentation.Hosts.HostsController).Assembly,
        typeof(Modules.Pets.Domain.Pets.Pet).Assembly,
        typeof(Modules.Pets.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Audit.Domain.Entries.AuditEntry).Assembly,
        typeof(Modules.Audit.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Bookings.Domain.Bookings.Booking).Assembly,
        typeof(Modules.Bookings.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Conversations.Domain.Conversations.Conversation).Assembly,
        typeof(Modules.Conversations.Infrastructure.DependencyInjection).Assembly,
    ];

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";

    [Fact]
    public void ListingsModule_Should_NotReferenceOtherModules()
    {
        foreach (var assembly in ListingsAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Auth", "PetHost.Modules.Owners", "PetHost.Modules.Hosts", "PetHost.Modules.Pets", "PetHost.Modules.Audit", "PetHost.Modules.Bookings", "PetHost.Modules.Conversations")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} só fala com outro módulo por Shared.Contracts (§5)");
        }
    }

    [Fact]
    public void OtherModules_Should_NotReferenceListings()
    {
        foreach (var assembly in OtherModules)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("PetHost.Modules.Listings")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar o Listings (§5)");
        }
    }

    [Fact]
    public void ListingsDomain_Should_NotDependOnFrameworksOrOuterLayers()
    {
        Types.InAssembly(ListingsDomain)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Listings.Application",
                "PetHost.Modules.Listings.Infrastructure",
                "PetHost.Modules.Listings.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ListingsApplication_Should_NotDependOnInfrastructureOrPresentation()
    {
        Types.InAssembly(ListingsApplication)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "System.Net.Http",
                "PetHost.Modules.Listings.Infrastructure",
                "PetHost.Modules.Listings.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ListingsPresentation_Should_NotDependOnDomainOrInfrastructure()
    {
        Types.InAssembly(ListingsPresentation)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Modules.Listings.Domain", "PetHost.Modules.Listings.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ListingsInfrastructure_Should_NotDependOnPresentation()
    {
        Types.InAssembly(ListingsInfrastructure)
            .Should()
            .NotHaveDependencyOn("PetHost.Modules.Listings.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ListingsHandlersAndValidators_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(ListingsApplication)
            .That().HaveNameEndingWith("Handler").Or().HaveNameEndingWith("Validator").Or().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
