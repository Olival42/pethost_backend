using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Bookings.Application.Bookings.CreateBooking;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Presentation.Bookings;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Regras do §4 para o módulo Bookings (reservas) e as fronteiras do §5: o Bookings acha tutor,
/// anfitrião, pet e cantinho só pelos contratos de <c>Shared.Contracts</c>, e ninguém referencia o Bookings.
/// </summary>
public sealed class BookingsModuleBoundaryTests
{
    private static readonly Assembly BookingsDomain = typeof(Booking).Assembly;
    private static readonly Assembly BookingsApplication = typeof(CreateBookingCommand).Assembly;
    private static readonly Assembly BookingsInfrastructure = typeof(Modules.Bookings.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly BookingsPresentation = typeof(BookingsController).Assembly;

    private static readonly Assembly[] BookingsAssemblies = [BookingsDomain, BookingsApplication, BookingsInfrastructure, BookingsPresentation];

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
        typeof(Modules.Audit.Domain.Entries.AuditEntry).Assembly,
        typeof(Modules.Audit.Infrastructure.DependencyInjection).Assembly,
    ];

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";

    [Fact]
    public void BookingsModule_Should_NotReferenceOtherModules()
    {
        foreach (var assembly in BookingsAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Auth", "PetHost.Modules.Owners", "PetHost.Modules.Hosts", "PetHost.Modules.Pets", "PetHost.Modules.Listings", "PetHost.Modules.Audit")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} só fala com outro módulo por Shared.Contracts (§5)");
        }
    }

    [Fact]
    public void OtherModules_Should_NotReferenceBookings()
    {
        foreach (var assembly in OtherModules)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("PetHost.Modules.Bookings")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar o Bookings (§5)");
        }
    }

    [Fact]
    public void BookingsDomain_Should_NotDependOnFrameworksOrOuterLayers()
    {
        Types.InAssembly(BookingsDomain)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Bookings.Application",
                "PetHost.Modules.Bookings.Infrastructure",
                "PetHost.Modules.Bookings.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void BookingsApplication_Should_NotDependOnInfrastructureOrPresentation()
    {
        Types.InAssembly(BookingsApplication)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "System.Net.Http",
                "PetHost.Modules.Bookings.Infrastructure",
                "PetHost.Modules.Bookings.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void BookingsPresentation_Should_NotDependOnDomainOrInfrastructure()
    {
        Types.InAssembly(BookingsPresentation)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Modules.Bookings.Domain", "PetHost.Modules.Bookings.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void BookingsInfrastructure_Should_NotDependOnPresentation()
    {
        Types.InAssembly(BookingsInfrastructure)
            .Should()
            .NotHaveDependencyOn("PetHost.Modules.Bookings.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void BookingsHandlersAndValidators_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(BookingsApplication)
            .That().HaveNameEndingWith("Handler").Or().HaveNameEndingWith("Validator").Or().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
