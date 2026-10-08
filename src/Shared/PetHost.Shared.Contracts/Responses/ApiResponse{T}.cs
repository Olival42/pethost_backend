namespace PetHost.Shared.Contracts.Responses;

/// <summary>Envelope padrão de resposta da API PetHost. Toda resposta usa este formato (§7).</summary>
public record ApiResponse<T>(bool Success, T? Data, ErrorResponse? Error, DateTimeOffset Timestamp)
{
    public static ApiResponse<T> Ok(T data) => new(true, data, null, DateTimeOffset.UtcNow);
    public static ApiResponse<T> Fail(ErrorResponse error) => new(false, default, error, DateTimeOffset.UtcNow);
}
