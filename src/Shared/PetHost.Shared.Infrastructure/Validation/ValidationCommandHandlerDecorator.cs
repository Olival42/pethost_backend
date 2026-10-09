using FluentValidation;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Infrastructure.Validation;

/// <summary>
/// Intercepta o handler, acumula as falhas do FluentValidation como
/// <see cref="Error.Validation"/> e retorna antes de executar o caso de uso.
/// O handler nunca chama o validador (§10).
/// </summary>
public sealed class ValidationCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var errors = new List<Error>();

        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);

            errors.AddRange(validation.Errors.Select(failure =>
                Error.Validation(ValidationFieldNames.ToCamelCase(failure.PropertyName), failure.ErrorMessage)));
        }

        return errors.Count > 0
            ? Result<TResponse>.Failure(errors)
            : await inner.HandleAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
