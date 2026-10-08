using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Kernel.Results;

/// <summary>Resultado de uma operação que pode falhar por regra de negócio (§6).</summary>
public class Result
{
    protected Result(bool isSuccess, List<Error>? errors)
    {
        if (isSuccess && errors is { Count: > 0 })
            throw new InvalidOperationException("A successful result cannot carry errors.");
        if (!isSuccess && errors is null or { Count: 0 })
            throw new InvalidOperationException("A failed result must carry at least one error.");

        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    /// <summary><c>null</c> quando <see cref="IsSuccess"/>.</summary>
    public List<Error>? Errors { get; }

    public Error? FirstError => Errors is { Count: > 0 } ? Errors[0] : null;

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, [error]);
    public static Result Failure(IEnumerable<Error> errors) => new(false, [.. errors]);

    public static implicit operator Result(Error error) => Failure(error);
}
