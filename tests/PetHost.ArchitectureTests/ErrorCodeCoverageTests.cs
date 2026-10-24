using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Validators;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Errors;
using Xunit;

namespace PetHost.ArchitectureTests;

/// <summary>
/// Garante o contrato de códigos de erro (tarefa 090): toda regra de validação tem um código estável
/// declarado em <c>*Errors</c>, e nenhum código significa duas coisas. O enum <c>ApiErrorCode</c> do
/// OpenAPI sai do mesmo catálogo (<see cref="ErrorCatalog"/>), então o que este teste cobre é o que o
/// front recebe.
/// </summary>
public sealed class ErrorCodeCoverageTests
{
    private static readonly Regex CodePattern = new("^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex TopLevelCodePattern = new("^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly IReadOnlyList<ErrorCatalogEntry> Catalog = ErrorCatalog.DiscoverLoaded();

    [Fact]
    public void Catalog_Should_FindTheErrorsOfEveryModule()
    {
        var sources = Catalog.Select(entry => entry.Source.Split('.')[0]).ToHashSet();

        sources.Should().Contain(["AuthErrors", "BookingsErrors", "ConversationsErrors", "HostsErrors", "ListingsErrors", "OwnersErrors", "PaymentsErrors", "PetsErrors", "ReviewsErrors", "AuditErrors", "ImageErrors", "ErrorCodes"]);
        Catalog.Count(entry => entry.IsValidation).Should().BeGreaterThan(250);
    }

    [Fact]
    public void Catalog_Should_UseWellFormedCodes()
    {
        Catalog.Where(entry => entry.IsValidation && !CodePattern.IsMatch(entry.Code))
            .Select(entry => $"{entry.Source}: '{entry.Code}'")
            .Should().BeEmpty("a validation reason is MODULE_FIELD_RULE in SCREAMING_SNAKE_CASE");

        Catalog.Where(entry => !entry.IsValidation && !TopLevelCodePattern.IsMatch(entry.Code))
            .Select(entry => $"{entry.Source}: '{entry.Code}'")
            .Should().BeEmpty();
    }

    [Fact]
    public void Catalog_Should_NotUseAGenericCodeForABusinessRule()
    {
        // Validação tem código próprio por regra; só estes códigos transversais ficam sem módulo.
        var generic = new[] { "VALIDATION_ERROR", "NOT_FOUND", "CONFLICT", "UNAUTHORIZED", "FORBIDDEN", "BUSINESS_RULE_VIOLATION", "UNEXPECTED_ERROR" };

        Catalog.Where(entry => entry.IsValidation && generic.Contains(entry.Code))
            .Select(entry => entry.Source)
            .Should().BeEmpty("a validation rule must not reuse a generic code");
    }

    [Fact]
    public void Catalog_Should_NotReuseACodeWithAnotherMeaning()
    {
        var conflicts = Catalog
            .GroupBy(entry => entry.Code)
            .Where(group => group.Count() > 1)
            .Where(group => !SameMeaning(group.ToList()))
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(entry => entry.Source))}")
            .ToList();

        conflicts.Should().BeEmpty("a code is a public contract: the same validation code must keep the same field, parameters and sentence");
    }

    [Fact]
    public void Validators_Should_DeclareAStableCodeOnEveryRule()
    {
        var validationCodes = Catalog.Where(entry => entry.IsValidation).Select(entry => entry.Code).ToHashSet();
        var validatorTypes = ValidatorTypes();
        validatorTypes.Should().HaveCountGreaterThan(80);

        var missing = new List<string>();
        foreach (var type in validatorTypes)
        {
            var validator = (IValidator)Instantiate(type);
            foreach (var group in validator.CreateDescriptor().GetMembersWithValidators())
            {
                foreach (var (propertyValidator, component) in group)
                {
                    // CustomErrors(...) só acrescenta falha por AddFailure(Error), que exige o motivo.
                    if (propertyValidator.Name == ValidationErrorExtensions.CustomErrorsValidatorName)
                        continue;

                    // Validador filho (SetValidator/ForEach): as regras dele são conferidas como validador próprio.
                    if (propertyValidator is IChildValidatorAdaptor)
                        continue;

                    if (component.ErrorCode is null || !validationCodes.Contains(component.ErrorCode))
                        missing.Add($"{type.Name}.{group.Key} ({propertyValidator.Name}/{propertyValidator.GetType().Name}): code '{component.ErrorCode}'");
                }
            }
        }

        missing.Should().BeEmpty("every rule must use WithError(<Module>Errors.X) so the failure carries a stable code");
    }

    [Fact]
    public void Source_Should_NotBuildValidationFailuresWithoutACode()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.EndsWith("ValidationErrorExtensions.cs", StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(file);
            if (text.Contains(".WithMessage(", StringComparison.Ordinal)
                || text.Contains(".Custom(", StringComparison.Ordinal)
                || text.Contains(".CustomAsync(", StringComparison.Ordinal)
                || text.Contains("new ValidationFailure(", StringComparison.Ordinal)
                || Regex.IsMatch(text, @"\.AddFailure\([^;]*?,", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                offenders.Add(Path.GetRelativePath(root, file));
        }

        offenders.Should().BeEmpty("use WithError(Error), CustomErrors and AddFailure(Error): text and code come from the module's *Errors class");
    }

    [Fact]
    public void Catalog_Should_NeverExposePersonalDataInParams()
    {
        // Id de recurso e valor de regra podem; nome de pessoa ou de pet, contato e documento, não.
        var personal = new[] { "name", "fullName", "petName", "email", "phone", "cpf", "cnpj", "address", "password", "token", "birthDate" };

        Catalog.SelectMany(entry => entry.ParamNames.Select(param => (entry.Source, Param: param)))
            .Where(item => personal.Contains(item.Param))
            .Select(item => $"{item.Source}: '{item.Param}'")
            .Should().BeEmpty("error params carry limits, dates and resource ids, never personal data");
    }

    [Fact]
    public void Catalog_Should_CarryParamsOnTheTopLevelErrorsThatQuoteValues()
    {
        var byCode = Catalog.Where(entry => !entry.IsValidation).GroupBy(entry => entry.Code).ToDictionary(group => group.Key, group => group.First().ParamNames);

        byCode["BOOKING_WEEKDAY_NOT_AVAILABLE"].Should().Equal("date");
        byCode["BOOKING_CAPACITY_EXCEEDED"].Should().Equal("date");
        byCode["BOOKING_DATES_BLOCKED"].Should().Equal("date");
        byCode["BOOKING_PET_PERIOD_CONFLICT"].Should().Equal("petId");
        byCode["PAYMENT_INSTALLMENT_ALREADY_PAID"].Should().Equal("number");
        byCode["LISTING_PHOTO_LIMIT_REACHED"].Should().Equal("max");
    }

    [Fact]
    public void ApiErrorCode_Should_ListEachCodeOnce()
    {
        var codes = ErrorCatalog.Codes(Catalog);

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().Contain(["VALIDATION_ERROR", "BOOKING_CAPACITY_EXCEEDED", "BOOKING_CHECK_IN_TOO_SOON", "BOOKING_CARE_TERMS_NOT_ACCEPTED", "AUTH_EMAIL_REQUIRED", "PET_NAME_TOO_LONG"]);
    }

    private static bool SameMeaning(List<ErrorCatalogEntry> entries)
    {
        var first = entries[0];

        return entries.All(entry =>
            entry.IsValidation == first.IsValidation
            && entry.Field == first.Field
            && entry.ParamNames.SequenceEqual(first.ParamNames)
            && (!entry.IsValidation || entry.HasArguments || first.HasArguments || entry.Message == first.Message));
    }

    private static List<Type> ValidatorTypes() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("PetHost.", StringComparison.Ordinal) == true)
            .SelectMany(SafeTypes)
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IValidator).IsAssignableFrom(type) && !type.IsGenericTypeDefinition)];

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    /// <summary>Cria o validador; dependências (relógio, portas) só precisam existir, a estrutura das regras não as usa.</summary>
    private static object Instantiate(Type type)
    {
        var constructor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(parameter => Argument(parameter.ParameterType)).ToArray();

        return constructor.Invoke(arguments);
    }

    private static object? Argument(Type type)
    {
        if (type == typeof(TimeProvider))
            return TimeProvider.System;

        if (type.IsInterface)
        {
            var create = typeof(DispatchProxy).GetMethods().Single(m => m.Name == nameof(DispatchProxy.Create) && m.IsGenericMethodDefinition).MakeGenericMethod(type, typeof(NullProxy));
            return create.Invoke(null, null);
        }

        throw new InvalidOperationException($"Cannot build the validator dependency '{type.Name}'.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PetHost.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("PetHost.slnx was not found above the test binaries.");
    }

    /// <summary>Stand-in de uma porta: devolve o valor padrão e nunca é chamado pela estrutura das regras.</summary>
    public class NullProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is { ReturnType.IsValueType: true } && targetMethod.ReturnType != typeof(void)
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
    }
}
