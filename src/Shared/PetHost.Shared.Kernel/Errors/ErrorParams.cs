using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace PetHost.Shared.Kernel.Errors;

/// <summary>
/// Parâmetros de uma regra de validação: só o que a frase precisa (limites, formatos), com nome em
/// camelCase e valor número, texto ou booleano. Imutável, com igualdade por conteúdo.
/// </summary>
/// <remarks>
/// Nunca dado pessoal nem o valor enviado pela pessoa: o tipo restrito e o nome fixo tornam isso
/// revisável no ponto onde o erro é declarado.
/// </remarks>
public sealed class ErrorParams : IEquatable<ErrorParams>
{
    private static readonly Regex NamePattern = new(
        "^[a-z][a-zA-Z0-9]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private ErrorParams(SortedDictionary<string, object> values) =>
        Values = new ReadOnlyDictionary<string, object>(values);

    /// <summary>Nome → valor, em ordem alfabética dos nomes.</summary>
    public IReadOnlyDictionary<string, object> Values { get; }

    public static ErrorParams Of(params (string Name, object Value)[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var values = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in items)
        {
            if (!NamePattern.IsMatch(name ?? string.Empty))
                throw new ArgumentException($"Parameter name '{name}' must be camelCase.", nameof(items));

            if (value is not (string or bool or int or long or short or byte or decimal or double))
                throw new ArgumentException($"Parameter '{name}' must be a number, a string or a boolean.", nameof(items));

            if (!values.TryAdd(name!, value))
                throw new ArgumentException($"Parameter '{name}' is repeated.", nameof(items));
        }

        return new ErrorParams(values);
    }

    public bool Equals(ErrorParams? other) =>
        other is not null
        && Values.Count == other.Values.Count
        && Values.All(pair => other.Values.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));

    public override bool Equals(object? obj) => Equals(obj as ErrorParams);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (name, value) in Values)
        {
            hash.Add(name, StringComparer.Ordinal);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
