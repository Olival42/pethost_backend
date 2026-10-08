namespace PetHost.Shared.Kernel.Results;

/// <summary>Composição de <see cref="Result{T}"/> sem checagem manual de <c>IsSuccess</c> (§6).</summary>
public static class ResultExtensions
{
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> map) =>
        result.IsFailure
            ? Result<TOut>.FromFailure(result)
            : Result<TOut>.Success(map(result.Value!));

    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Task<Result<TOut>>> bind)
    {
        return result.IsFailure
            ? Result<TOut>.FromFailure(result)
            : await bind(result.Value!).ConfigureAwait(false);
    }

    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result<TOut>>> bind)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(bind).ConfigureAwait(false);
    }
}
