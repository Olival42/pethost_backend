using FluentValidation;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>
/// Liga o validador de entrada à regra do value object, em vez de repeti-la:
/// a regra mora só no <c>Create</c> do domínio.
/// </summary>
public static class ValueObjectRules
{
    /// <summary>
    /// Falha com as mensagens que <paramref name="create"/> devolver, todas no campo
    /// validado. Para value objects de um campo só (telefone, nome...).
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, string?> MustBeValid<T, TValue>(
        this IRuleBuilder<T, string?> ruleBuilder,
        Func<string?, Result<TValue>> create) =>
        ruleBuilder.Custom((value, context) =>
        {
            var result = create(value);
            if (result.IsSuccess)
                return;

            foreach (var error in result.Errors!)
                context.AddFailure(error.Message);
        });

    /// <summary>
    /// Para value objects de vários campos (endereço): cada erro sai no campo que o
    /// próprio domínio indicou (<c>address.zipCode</c>, <c>address.city</c>...).
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, TInput?> MustBeValidComposite<T, TInput, TValue>(
        this IRuleBuilder<T, TInput?> ruleBuilder,
        Func<TInput, Result<TValue>> create)
        where TInput : class =>
        ruleBuilder.Custom((value, context) =>
        {
            if (value is null)
                return;

            var result = create(value);
            if (result.IsSuccess)
                return;

            foreach (var error in result.Errors!)
                context.AddFailure(error.Field ?? context.PropertyPath, error.Message);
        });
}
