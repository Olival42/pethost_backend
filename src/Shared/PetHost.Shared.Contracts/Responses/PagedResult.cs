namespace PetHost.Shared.Contracts.Responses;

/// <summary>Página de uma lista (§13): <c>page</c> começa em 1.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
