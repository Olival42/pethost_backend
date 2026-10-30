using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Hosts.Application.HostApplications.StartHostApplication;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Presentation.Hosts;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Regras do §4 para as camadas do módulo Hosts e a fronteira do §5: o Hosts fala com a
/// conta (Auth) e com a trilha (Audit) só por <c>Shared.Contracts</c>.
/// </summary>
public sealed class HostsModuleBoundaryTests
{
    private static readonly Assembly HostsDomain = typeof(Host).Assembly;
    private static readonly Assembly HostsApplication = typeof(StartHostApplicationCommand).Assembly;
    private static readonly Assembly HostsInfrastructure = typeof(Modules.Hosts.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly HostsPresentation = typeof(HostsController).Assembly;

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";

    [Fact]
    public void HostsModule_Should_NotReferenceOtherModules()
    {
        foreach (var assembly in new[] { HostsDomain, HostsApplication, HostsInfrastructure, HostsPresentation })
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny("PetHost.Modules.Auth", "PetHost.Modules.Owners", "PetHost.Modules.Pets", "PetHost.Modules.Audit", "PetHost.Modules.Conversations")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} só fala com outro módulo por Shared.Contracts (§5)");
        }
    }

    [Fact]
    public void HostsApplication_Should_NotDependOnInfrastructureOrPresentation()
    {
        Types.InAssembly(HostsApplication)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Hosts.Infrastructure",
                "PetHost.Modules.Hosts.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void HostsDomain_Should_NotDependOnApplication()
    {
        Types.InAssembly(HostsDomain)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Modules.Hosts.Application", "PetHost.Modules.Hosts.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void HostsPresentation_Should_NotDependOnDomainOrInfrastructure()
    {
        Types.InAssembly(HostsPresentation)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Modules.Hosts.Domain", "PetHost.Modules.Hosts.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void HostsInfrastructure_Should_NotDependOnPresentation()
    {
        Types.InAssembly(HostsInfrastructure)
            .Should()
            .NotHaveDependencyOn("PetHost.Modules.Hosts.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void HostsHandlersAndValidators_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(HostsApplication)
            .That().HaveNameEndingWith("Handler").Or().HaveNameEndingWith("Validator").Or().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
