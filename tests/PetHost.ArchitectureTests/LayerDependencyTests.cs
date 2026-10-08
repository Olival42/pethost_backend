using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using PetHost.Modules.Auth.Application.Sessions.CreateSession;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure;
using PetHost.Modules.Auth.Presentation.Sessions;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Verifica as regras da seção 4. Violação aqui quebra o build, que é o
/// objetivo: o compilador já barra a maior parte, e isto cobre o resto.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly Kernel = typeof(Result).Assembly;
    private static readonly Assembly Contracts = typeof(ApiResponse<>).Assembly;
    private static readonly Assembly Domain = typeof(User).Assembly;
    private static readonly Assembly Application = typeof(CreateSessionCommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Presentation = typeof(SessionsController).Assembly;

    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";
    private const string Redis = "StackExchange.Redis";

    [Fact]
    public void Kernel_Should_NotDependOnAnyOtherPetHostProject()
    {
        Types.InAssembly(Kernel)
            .Should()
            .NotHaveDependencyOnAny("PetHost.Shared.Contracts", "PetHost.Modules", "PetHost.Api")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Kernel_Should_NotDependOnFrameworks()
    {
        Types.InAssembly(Kernel)
            .Should()
            .NotHaveDependencyOnAny(EntityFramework, AspNetCore, Npgsql, Redis)
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Contracts_Should_NotDependOnAspNetCore()
    {
        // Contracts e referenciado pela Application; trazer ASP.NET aqui vazaria
        // web para dentro da camada de casos de uso.
        Types.InAssembly(Contracts)
            .Should()
            .NotHaveDependencyOnAny(AspNetCore, EntityFramework)
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_Should_NotDependOnInfrastructureConcerns()
    {
        Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(EntityFramework, AspNetCore, Npgsql, Redis)
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_Should_NotDependOnOuterLayers()
    {
        Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "PetHost.Modules.Auth.Application",
                "PetHost.Modules.Auth.Infrastructure",
                "PetHost.Modules.Auth.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_Should_NotDependOnInfrastructureConcerns()
    {
        Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny(EntityFramework, AspNetCore, Npgsql, Redis)
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_Should_NotDependOnInfrastructureOrPresentation()
    {
        Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny(
                "PetHost.Modules.Auth.Infrastructure",
                "PetHost.Modules.Auth.Presentation",
                "PetHost.Shared.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Presentation_Should_NotDependOnDomainOrInfrastructure()
    {
        // A secao 4 e explicita: o controller so conhece Commands e DTOs, o que
        // impede serializar entidade de dominio na resposta.
        Types.InAssembly(Presentation)
            .Should()
            .NotHaveDependencyOnAny(
                "PetHost.Modules.Auth.Domain",
                "PetHost.Modules.Auth.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Infrastructure_Should_NotDependOnPresentation()
    {
        Types.InAssembly(Infrastructure)
            .Should()
            .NotHaveDependencyOn("PetHost.Modules.Auth.Presentation")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Roles_Should_MatchDomainWireValues_When_Compared()
    {
        // Shared.Contracts.Roles duplica os valores de UserRoleValues porque a
        // Presentation nao pode referenciar Domain. Este teste existe para que a
        // duplicacao nao saia de sincronia sem ninguem perceber.
        Roles.Owner.Should().Be(UserRoleValues.Owner);
        Roles.Host.Should().Be(UserRoleValues.Host);
        Roles.Admin.Should().Be(UserRoleValues.Admin);
    }

    [Fact]
    public void Roles_Should_CoverEveryUserRole_When_EnumGrows()
    {
        var declared = typeof(Roles)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        var fromEnum = Enum.GetValues<UserRole>().Select(UserRoleValues.ToWire);

        declared.Should().BeEquivalentTo(fromEnum);
    }
}

public sealed class ConventionTests
{
    private static readonly Assembly Domain = typeof(User).Assembly;
    private static readonly Assembly Application = typeof(CreateSessionCommand).Assembly;
    private static readonly Assembly Presentation = typeof(SessionsController).Assembly;

    [Fact]
    public void Handlers_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(Application)
            .That().HaveNameEndingWith("Handler")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Controllers_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(Presentation)
            .That().HaveNameEndingWith("Controller")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Validators_Should_BeSealed_When_Declared()
    {
        Types.InAssembly(Application)
            .That().HaveNameEndingWith("Validator")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void RepositoryInterfaces_Should_LiveInDomain_When_Declared()
    {
        Types.InAssembly(Domain)
            .That().AreInterfaces().And().HaveNameEndingWith("Repository")
            .Should().BePublic()
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Commands_Should_BeSealedRecords_When_Declared()
    {
        Types.InAssembly(Application)
            .That().HaveNameEndingWith("Command")
            .Should().BeSealed()
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
