using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Modules.Owners.Infrastructure;
using PetHost.Modules.Owners.Presentation.Owners;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Regras do §4 para o módulo Owners e as fronteiras do §5 entre módulos: nenhum
/// módulo referencia outro; conversa só por <c>Shared.Contracts</c>.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly OwnersDomain = typeof(Owner).Assembly;
    private static readonly Assembly OwnersApplication = typeof(RegisterOwnerAccountCommand).Assembly;
    private static readonly Assembly OwnersInfrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly OwnersPresentation = typeof(OwnersController).Assembly;

    private static readonly Assembly[] AuthAssemblies =
    [
        typeof(Modules.Auth.Domain.Users.User).Assembly,
        typeof(Modules.Auth.Application.Sessions.CreateSession.CreateSessionCommand).Assembly,
        typeof(Modules.Auth.Infrastructure.DependencyInjection).Assembly,
        typeof(Modules.Auth.Presentation.Sessions.SessionsController).Assembly,
    ];

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";

    [Fact]
    public void OwnersModule_Should_NotReferenceAuthModule()
    {
        foreach (var assembly in new[] { OwnersDomain, OwnersApplication, OwnersInfrastructure, OwnersPresentation })
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("PetHost.Modules.Auth")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar o Auth (§5)");
        }
    }

    [Fact]
    public void AuthModule_Should_NotReferenceOwnersModule()
    {
        foreach (var assembly in AuthAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("PetHost.Modules.Owners")
                .GetResult().IsSuccessful.Should().BeTrue($"{assembly.GetName().Name} não pode referenciar o Owners (§5)");
        }
    }

    [Fact]
    public void OwnersDomain_Should_NotDependOnFrameworksOrOuterLayers()
    {
        Types.InAssembly(OwnersDomain)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Owners.Application",
                "PetHost.Modules.Owners.Infrastructure",
                "PetHost.Modules.Owners.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void OwnersApplication_Should_NotDependOnInfrastructureOrPresentation()
    {
        Types.InAssembly(OwnersApplication)
            .Should()
            .NotHaveDependencyOnAny(
                EntityFramework,
                AspNetCore,
                Npgsql,
                "PetHost.Modules.Owners.Infrastructure",
                "PetHost.Modules.Owners.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void OwnersPresentation_Should_NotDependOnDomainOrInfrastructure()
    {
        Types.InAssembly(OwnersPresentation)
            .Should()
            .NotHaveDependencyOnAny(
                "PetHost.Modules.Owners.Domain",
                "PetHost.Modules.Owners.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void OwnersInfrastructure_Should_NotDependOnPresentation()
    {
        Types.InAssembly(OwnersInfrastructure)
            .Should()
            .NotHaveDependencyOn("PetHost.Modules.Owners.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
