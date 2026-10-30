using FluentValidation;
using FluentValidation.Validators;
using FluentValidation.Results;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Contracts.Validation;

/// <summary>
/// Liga as regras do FluentValidation aos erros declarados em <c>*Errors</c>: a mensagem, o motivo
/// estável (<see cref="Error.Reason"/>) e os parâmetros saem do mesmo <see cref="Error"/>, sem repetir
/// texto nem código no validador. O <c>ValidationCommandHandlerDecorator</c> lê o erro de volta.
/// </summary>
public static class ValidationErrorExtensions
{
    /// <summary>Nome do validador de <see cref="CustomErrors{T,TProperty}"/>: o teste de cobertura sabe que os códigos dele vêm de <c>AddFailure(Error)</c>.</summary>
    public const string CustomErrorsValidatorName = "CustomErrorsValidator";

    /// <summary>A regra falha com <paramref name="error"/> (mensagem, motivo e parâmetros dele).</summary>
    public static IRuleBuilderOptions<T, TProperty> WithError<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(error);
        RequireReason(error);

        return rule.WithMessage(error.Message).WithErrorCode(error.Reason!).WithState(_ => error);
    }

    /// <summary>
    /// Regra de código livre: o <paramref name="action"/> acrescenta as falhas com <see cref="AddFailure{T}"/>
    /// (cada uma leva o código do seu <see cref="Error"/>). Use no lugar de <c>Custom</c>, que o teste de
    /// cobertura de códigos não consegue conferir por fora.
    /// </summary>
    public static IRuleBuilderOptions<T, TProperty> CustomErrors<T, TProperty>(
        this IRuleBuilder<T, TProperty> ruleBuilder,
        Action<TProperty, ValidationContext<T>> action)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        ArgumentNullException.ThrowIfNull(action);

        return ruleBuilder.SetValidator(new CustomErrorsValidator<T, TProperty>(action));
    }

    /// <summary>Como <see cref="CustomErrors{T,TProperty}"/>, para regra que consulta algo de forma assíncrona.</summary>
    public static IRuleBuilderOptions<T, TProperty> CustomErrorsAsync<T, TProperty>(
        this IRuleBuilder<T, TProperty> ruleBuilder,
        Func<TProperty, ValidationContext<T>, CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        ArgumentNullException.ThrowIfNull(action);

        return ruleBuilder.SetAsyncValidator(new CustomErrorsAsyncValidator<T, TProperty>(action));
    }

    /// <summary>Acrescenta a falha de <paramref name="error"/> no campo que ele indica (ou no da regra em curso, se não indicar).</summary>
    public static void AddFailure<T>(this ValidationContext<T> context, Error error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        context.AddFailureAt(error.Field is null ? context.PropertyPath : context.PropertyChain.BuildPropertyPath(error.Field), error);
    }

    /// <summary>Acrescenta a falha de <paramref name="error"/> no campo da regra em curso, ignorando o campo que o erro declara.</summary>
    public static void AddFailureToRuleProperty<T>(this ValidationContext<T> context, Error error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        context.AddFailureAt(context.PropertyPath, error);
    }

    private static void AddFailureAt<T>(this ValidationContext<T> context, string path, Error error)
    {
        RequireReason(error);

        context.AddFailure(new ValidationFailure(path, error.Message)
        {
            ErrorCode = error.Reason,
            CustomState = error,
        });
    }

    private static void RequireReason(Error error)
    {
        if (error.Code != ErrorCodes.Validation || string.IsNullOrEmpty(error.Reason))
            throw new ArgumentException($"'{error.Code}' is not a validation error with a reason; declare it with Error.Validation(field, reason, message).", nameof(error));
    }

    private sealed class CustomErrorsValidator<T, TProperty>(Action<TProperty, ValidationContext<T>> action) : PropertyValidator<T, TProperty>
    {
        public override string Name => CustomErrorsValidatorName;

        public override bool IsValid(ValidationContext<T> context, TProperty value)
        {
            action(value, context);
            return true;
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => string.Empty;
    }

    private sealed class CustomErrorsAsyncValidator<T, TProperty>(Func<TProperty, ValidationContext<T>, CancellationToken, Task> action) : AsyncPropertyValidator<T, TProperty>
    {
        public override string Name => CustomErrorsValidatorName;

        public override async Task<bool> IsValidAsync(ValidationContext<T> context, TProperty value, CancellationToken cancellation)
        {
            await action(value, context, cancellation).ConfigureAwait(false);
            return true;
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => string.Empty;
    }
}
