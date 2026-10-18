using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Reviews.Application.Reviews.CreateReview;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Modules.Reviews.Presentation.Reviews;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Regras do §4 para o módulo Reviews (avaliações) e as fronteiras do §5: o Reviews acha tutor,
/// conta e reserva só pelos contratos de <c>Shared.Contracts</c>, e ninguém referencia o Reviews.
/// </summary>
public sealed class ReviewsModuleBoundaryTests
{
    private static readonly Assembly ReviewsDomain = typeof(Review).Assembly;
    private static readonly Assembly ReviewsApplication = typeof(CreateReviewCommand).Assembly;
    private static readonly Assembly ReviewsInfrastructure = typeof(Modules.Reviews.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly ReviewsPresentation = typeof(ReviewsController).Assembly;

    private static readonly Assembly[] ReviewsAssemblies =
        [ReviewsDomain, ReviewsApplication, ReviewsInfrastructure, ReviewsPresentation];

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
        typeof(Modules.Bookings.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Conversations.Domain.Conversations.Conversation).Assembly,
        typeof(Modules.Conversations.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Audit.Domain.Entries.AuditEntry).Assembly,
        typeof(Modules.Audit.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Payments.Domain.Payments.Payment).Assembly,
        typeof(Modules.Payments.Infrastructure.DependencyInjection).Assembly,
    ];

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";

    [Fact]
    public void ReviewsModule_Should_NotReferenceOtherModules()
    {
        foreach (var assembly in ReviewsAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Auth", "PetHost.Modules.Owners", "PetHost.Modules.Hosts", "PetHost.Modules.Pets", "PetHost.Modules.Listings", "PetHost.Modules.Bookings", "PetHost.Modules.Conversations", "PetHost.Modules.Audit", "PetHost.Modules.Payments")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} só fala com outro módulo por Shared.Contracts (§5)");
        }
    }

    [Fact]
    public void OtherModules_Should_NotReferenceReviews()
    {
        foreach (var assembly in OtherModules)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("PetHost.Modules.Reviews")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar o Reviews (§5)");
        }
    }

    [Fact]
    public void ReviewsDomain_Should_NotDependOnFrameworksOrOuterLayers()
    {
        Types.InAssembly(ReviewsDomain)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Reviews.Application",
                "PetHost.Modules.Reviews.Infrastructure",
                "PetHost.Modules.Reviews.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ReviewsApplication_Should_NotDependOnInfrastructureOrPresentation()
    {
        Types.InAssembly(ReviewsApplication)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Reviews.Infrastructure",
                "PetHost.Modules.Reviews.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ReviewsPresentation_Should_NotDependOnDomainOrInfrastructure()
    {
        Types.InAssembly(ReviewsPresentation)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Modules.Reviews.Domain", "PetHost.Modules.Reviews.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ReviewsInfrastructure_Should_NotDependOnPresentation()
    {
        Types.InAssembly(ReviewsInfrastructure)
            .Should()
            .NotHaveDependencyOn("PetHost.Modules.Reviews.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ReviewsHandlersAndValidators_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(ReviewsApplication)
            .That().HaveNameEndingWith("Handler").Or().HaveNameEndingWith("Validator").Or().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
