using FluentValidation.Results;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Infrastructure.Validation;

/// <summary>
/// Falha do FluentValidation → <see cref="Error"/> de validação com o motivo estável. Único lugar
/// que faz essa tradução (o decorator e o <c>AccountRegistrar</c> usam o mesmo).
/// </summary>
public static class ValidationFailureMapper
{
    /// <exception cref="InvalidOperationException">
    /// A regra falhou sem passar por <c>WithError</c>/<c>AddFailure(Error)</c>, logo sem código estável: é
    /// bug do validador (o teste de cobertura de códigos pega antes de chegar aqui).
    /// </exception>
    public static IEnumerable<Error> ToErrors(this ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Errors.Select(ToError);
    }

    public static Error ToError(ValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (failure.CustomState is not Error { Reason: not null } declared)
            throw new InvalidOperationException(
                $"The validation rule for '{failure.PropertyName}' ('{failure.ErrorCode}') has no stable error code. Declare it in the module's *Errors class and use WithError(...).");

        return declared with { Field = ValidationFieldNames.ToCamelCase(failure.PropertyName) };
    }
}
