using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Pets.Application.Pets.RegisterPet;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Modules.Pets.Presentation.Pets;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Regras do §4 para os módulos Pets e Hosts, e as fronteiras do §5: o Pets acha tutor,
/// anfitrião e conta só pelos contratos de <c>Shared.Contracts</c>, e ninguém referencia
/// o Pets nem o Hosts.
/// </summary>
public sealed class PetsModuleBoundaryTests
{
    private static readonly Assembly PetsDomain = typeof(Pet).Assembly;
    private static readonly Assembly PetsApplication = typeof(RegisterPetCommand).Assembly;
    private static readonly Assembly PetsInfrastructure = typeof(Modules.Pets.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly PetsPresentation = typeof(PetsController).Assembly;

    private static readonly Assembly HostsDomain = typeof(Modules.Hosts.Domain.Hosts.Host).Assembly;
    private static readonly Assembly HostsInfrastructure = typeof(Modules.Hosts.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly[] PetsAssemblies = [PetsDomain, PetsApplication, PetsInfrastructure, PetsPresentation];
    private static readonly Assembly[] HostsAssemblies = [HostsDomain, HostsInfrastructure];

    private static readonly Assembly[] OtherModules =
    [
        typeof(Modules.Auth.Domain.Users.User).Assembly,
        typeof(Modules.Auth.Application.Sessions.CreateSession.CreateSessionCommand).Assembly,
        typeof(Modules.Auth.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Auth.Presentation.Sessions.SessionsController).Assembly,
        typeof(Modules.Owners.Domain.Owners.Owner).Assembly,
        typeof(Modules.Owners.Application.Owners.RegisterOwnerAccount.RegisterOwnerAccountCommand).Assembly,
        typeof(Modules.Owners.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Owners.Presentation.Owners.OwnersController).Assembly,
        typeof(Modules.Audit.Domain.Entries.AuditEntry).Assembly,
        typeof(Modules.Audit.Infrastructure.DependencyInjection).Assembly,
    ];

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";

    [Fact]
    public void PetsModule_Should_NotReferenceOtherModules()
    {
        foreach (var assembly in PetsAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Auth", "PetHost.Modules.Owners", "PetHost.Modules.Hosts", "PetHost.Modules.Audit")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} só fala com outro módulo por Shared.Contracts (§5)");
        }
    }

    [Fact]
    public void HostsModule_Should_NotReferenceOtherModules()
    {
        foreach (var assembly in HostsAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Auth", "PetHost.Modules.Owners", "PetHost.Modules.Pets", "PetHost.Modules.Audit")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar outro módulo (§5)");
        }
    }

    [Fact]
    public void OtherModules_Should_NotReferencePetsOrHosts()
    {
        foreach (var assembly in OtherModules)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Pets", "PetHost.Modules.Hosts")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar Pets nem Hosts (§5)");
        }
    }

    [Fact]
    public void PetsDomain_Should_NotDependOnFrameworksOrOuterLayers()
    {
        Types.InAssembly(PetsDomain)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Pets.Application",
                "PetHost.Modules.Pets.Infrastructure",
                "PetHost.Modules.Pets.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void HostsDomain_Should_NotDependOnFrameworksOrOuterLayers()
    {
        Types.InAssembly(HostsDomain)
            .Should()
            .NotHaveDependencyOnAny(EntityFramework, AspNetCore, Npgsql, "PetHost.Modules.Hosts.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PetsApplication_Should_NotDependOnInfrastructureOrPresentation()
    {
        Types.InAssembly(PetsApplication)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Pets.Infrastructure",
                "PetHost.Modules.Pets.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PetsPresentation_Should_NotDependOnDomainOrInfrastructure()
    {
        Types.InAssembly(PetsPresentation)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Modules.Pets.Domain", "PetHost.Modules.Pets.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PetsInfrastructure_Should_NotDependOnPresentation()
    {
        Types.InAssembly(PetsInfrastructure)
            .Should()
            .NotHaveDependencyOn("PetHost.Modules.Pets.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void PetsHandlersAndValidators_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(PetsApplication)
            .That().HaveNameEndingWith("Handler").Or().HaveNameEndingWith("Validator").Or().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
