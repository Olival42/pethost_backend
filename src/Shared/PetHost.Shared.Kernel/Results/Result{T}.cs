using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Kernel.Results;

/// <summary>Resultado que carrega um valor em caso de sucesso (§6).</summary>
public sealed class Result<T> : Result
{
    private Result(bool isSuccess, T? value, List<Error>? errors) : base(isSuccess, errors) => Value = value;

    /// <summary><c>default</c> quando <see cref="Result.IsFailure"/>.</summary>
    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static new Result<T> Failure(Error error) => new(false, default, [error]);
    public static new Result<T> Failure(IEnumerable<Error> errors) => new(false, default, [.. errors]);

    /// <summary>Propaga a falha de outro Result preservando todos os erros.</summary>
    public static Result<T> FromFailure(Result result) => new(false, default, result.Errors);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
