using System.Reflection;
using System.Runtime.CompilerServices;

namespace PetHost.Shared.Kernel.Errors;

/// <summary>De onde veio um código do catálogo e como ele é usado.</summary>
/// <param name="Code">O código estável: o <c>Code</c> do erro ou, na validação, o <c>Reason</c>.</param>
/// <param name="Source">Membro que declara o erro (<c>BookingsErrors.CheckInTooSoon</c>).</param>
/// <param name="IsValidation">Se é motivo de regra de validação (vem em <c>details[].errors[].code</c>).</param>
/// <param name="Field">Campo da validação, quando o erro é de validação.</param>
/// <param name="ParamNames">Nomes dos parâmetros que o erro leva, em ordem alfabética.</param>
/// <param name="Message">A frase do erro (nas fábricas com argumentos, a de uma chamada com valores neutros).</param>
/// <param name="HasArguments">Se o erro vem de uma fábrica com argumentos (<c>NotFound(BookingId id)</c>).</param>
public sealed record ErrorCatalogEntry(
    string Code,
    string Source,
    bool IsValidation,
    string? Field,
    IReadOnlyList<string> ParamNames,
    string Message,
    bool HasArguments);

/// <summary>
/// Todos os códigos de erro que a API pode devolver, descobertos por reflexão nas classes
/// <c>*Errors</c> e em <see cref="ErrorCodes"/>. Fonte única do enum <c>ApiErrorCode</c> do OpenAPI
/// e do teste que garante que nenhuma validação fica sem código: não há lista manual para divergir.
/// </summary>
public static class ErrorCatalog
{
    private const string ErrorsSuffix = "Errors";
    private const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Carrega os assemblies <c>PetHost.*</c> da pasta da aplicação e descobre os códigos neles.</summary>
    public static IReadOnlyList<ErrorCatalogEntry> DiscoverLoaded()
    {
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "PetHost.*.dll"))
        {
            var name = AssemblyName.GetAssemblyName(path);
            if (AppDomain.CurrentDomain.GetAssemblies().All(loaded => loaded.GetName().Name != name.Name))
                Assembly.Load(name);
        }

        return Discover(AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name?.StartsWith("PetHost.", StringComparison.Ordinal) == true));
    }

    public static IReadOnlyList<ErrorCatalogEntry> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var entries = new List<ErrorCatalogEntry>();

        foreach (var constant in typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.IsLiteral))
        {
            var code = (string)constant.GetRawConstantValue()!;
            entries.Add(new ErrorCatalogEntry(code, $"{nameof(ErrorCodes)}.{constant.Name}", false, null, [], string.Empty, false));
        }

        foreach (var type in assemblies.SelectMany(SafeTypes).Where(IsErrorsClass))
        {
            foreach (var member in type.GetFields(Statics).Where(field => field.FieldType == typeof(Error)))
                entries.Add(ToEntry(type, member.Name, (Error)member.GetValue(null)!, false));

            foreach (var member in type.GetProperties(Statics).Where(property => property.PropertyType == typeof(Error)))
                entries.Add(ToEntry(type, member.Name, (Error)member.GetValue(null)!, false));

            foreach (var method in type.GetMethods(Statics).Where(method => method.ReturnType == typeof(Error) && !method.IsSpecialName))
                entries.Add(ToEntry(type, method.Name, Invoke(type, method), true));
        }

        return entries;
    }

    /// <summary>Os códigos sem repetição, em ordem, como o enum do OpenAPI os lista.</summary>
    public static IReadOnlyList<string> Codes(IEnumerable<ErrorCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return [.. entries.Select(entry => entry.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static bool IsErrorsClass(Type type) =>
        type is { IsAbstract: true, IsSealed: true, IsNested: false }
        && type.Name.EndsWith(ErrorsSuffix, StringComparison.Ordinal)
        && type != typeof(Error)
        && !type.IsDefined(typeof(CompilerGeneratedAttribute), false);

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

    private static ErrorCatalogEntry ToEntry(Type type, string member, Error error, bool hasArguments)
    {
        var isValidation = error.Code == ErrorCodes.Validation;

        return new ErrorCatalogEntry(
            isValidation ? error.Reason ?? string.Empty : error.Code,
            $"{type.Name}.{member}",
            isValidation,
            isValidation ? error.Field : null,
            error.Params is null ? [] : [.. error.Params.Values.Keys],
            error.Message,
            hasArguments);
    }

    /// <summary>
    /// Fábrica de erro com argumentos (<c>NotFound(BookingId id)</c>): chama com valores neutros só para
    /// ler o código e os nomes dos parâmetros. As fábricas não consultam nada além dos argumentos.
    /// </summary>
    private static Error Invoke(Type type, MethodInfo method)
    {
        var arguments = method.GetParameters().Select(parameter => Neutral(parameter.ParameterType)).ToArray();

        try
        {
            return (Error)method.Invoke(null, arguments)!;
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException($"{type.Name}.{method.Name} could not be called with neutral arguments to read its error code.", exception.InnerException);
        }
    }

    private static object? Neutral(Type type) =>
        type == typeof(string) ? "x"
        : type.IsValueType ? Activator.CreateInstance(type)
        : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? Array.CreateInstance(type.GetGenericArguments()[0], 0)
        : RuntimeHelpers.GetUninitializedObject(type);
}
