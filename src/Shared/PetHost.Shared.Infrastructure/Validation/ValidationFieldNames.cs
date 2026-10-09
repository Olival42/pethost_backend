namespace PetHost.Shared.Infrastructure.Validation;

/// <summary>Nome de propriedade do FluentValidation → nome do campo no JSON.</summary>
public static class ValidationFieldNames
{
    /// <summary><c>CheckOut</c> → <c>checkOut</c>, para casar com o campo no JSON da request.</summary>
    public static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return propertyName;

        // Caminhos aninhados do FluentValidation ("Address.City") viram "address.city".
        var segments = propertyName.Split('.');

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length > 0 && char.IsUpper(segment[0]))
                segments[i] = char.ToLowerInvariant(segment[0]) + segment[1..];
        }

        return string.Join('.', segments);
    }
}
