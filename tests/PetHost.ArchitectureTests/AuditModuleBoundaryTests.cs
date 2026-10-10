using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Audit.Application.Entries.SearchAuditEntries;
using PetHost.Modules.Audit.Domain.Entries;
using PetHost.Modules.Audit.Presentation.Entries;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Regras do §4 para o módulo Audit e as fronteiras do §5: os módulos registram na
/// trilha só pelo contrato <c>IAuditTrail</c> de <c>Shared.Contracts</c>, nunca
/// referenciando o Audit — e o Audit não conhece ninguém.
/// </summary>
public sealed class AuditModuleBoundaryTests
{
    private static readonly Assembly AuditDomain = typeof(AuditEntry).Assembly;
    private static readonly Assembly AuditApplication = typeof(SearchAuditEntriesQuery).Assembly;
    private static readonly Assembly AuditInfrastructure = typeof(Modules.Audit.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly AuditPresentation = typeof(AuditEntriesController).Assembly;

    private static readonly Assembly[] AuditAssemblies = [AuditDomain, AuditApplication, AuditInfrastructure, AuditPresentation];

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
        typeof(Modules.Pets.Domain.Pets.Pet).Assembly,
        typeof(Modules.Pets.Application.Pets.RegisterPet.RegisterPetCommand).Assembly,
        typeof(Modules.Pets.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Pets.Presentation.Pets.PetsController).Assembly,
        typeof(Modules.Hosts.Domain.Hosts.Host).Assembly,
        typeof(Modules.Hosts.Infrastructure.DependencyInjection).Assembly,
    ];

    [Fact]
    public void OtherModules_Should_NotReferenceAuditModule()
    {
        foreach (var assembly in OtherModules)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("PetHost.Modules.Audit")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} registra na trilha só pelo IAuditTrail (§5)");
        }
    }

    [Fact]
    public void AuditModule_Should_NotReferenceOtherModules()
    {
        foreach (var assembly in AuditAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Auth", "PetHost.Modules.Owners", "PetHost.Modules.Pets", "PetHost.Modules.Hosts")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar outro módulo (§5)");
        }
    }

    [Fact]
    public void AuditDomain_Should_NotDependOnFrameworksOrOuterLayers()
    {
        Types.InAssembly(AuditDomain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Npgsql",
                "PetHost.Modules.Audit.Application",
                "PetHost.Modules.Audit.Infrastructure",
                "PetHost.Modules.Audit.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void AuditApplication_Should_NotDependOnInfrastructureOrPresentation()
    {
        Types.InAssembly(AuditApplication)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "PetHost.Modules.Audit.Infrastructure",
                "PetHost.Modules.Audit.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void AuditPresentation_Should_NotDependOnDomainOrInfrastructure()
    {
        Types.InAssembly(AuditPresentation)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Modules.Audit.Domain", "PetHost.Modules.Audit.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
