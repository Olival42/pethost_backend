using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Contracts.Responses;

/// <summary>
/// Único ponto de tradução <c>Result&lt;T&gt; → ApiResponse&lt;T&gt;</c>.
/// Nenhum controller monta envelope à mão (§7).
/// </summary>
public static class ApiResponseMapper
{
    public static ApiResponse<T> ToApiResponse<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return ApiResponse<T>.Ok(result.Value!);

        var errors = result.Errors ?? [];

        var grouped = errors
            .Where(e => e.Code == ErrorCodes.Validation)
            .GroupBy(e => e.Field ?? "general")
            .Select(g => new DataErrors(g.Key, [.. g.Select(e => e.Message)]))
            .ToList();

        if (grouped.Count > 0)
            return ApiResponse<T>.Fail(new ErrorResponse(ErrorCodes.Validation, "Validation failed", grouped));

        var first = errors.FirstOrDefault();
        return ApiResponse<T>.Fail(new ErrorResponse(
            first?.Code ?? ErrorCodes.Unexpected,
            first?.Message ?? "Unknown error"));
    }
}
