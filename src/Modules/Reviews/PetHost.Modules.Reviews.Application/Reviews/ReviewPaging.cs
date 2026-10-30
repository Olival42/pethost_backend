using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Reviews.Application.Reviews;

/// <summary>Paginação das listas (§13): <c>page</c> a partir de 1, <c>pageSize</c> de 1 a 100.</summary>
internal static class ReviewPaging
{
    /// <summary>Os erros da paginação (acumulados); lista vazia se está certa.</summary>
    public static List<Error> Validate(int page, int pageSize)
    {
        var errors = new List<Error>();

        if (page < 1)
            errors.Add(ReviewsErrors.PageInvalid);

        if (pageSize is < 1 or > ReviewsErrors.MaxPageSize)
            errors.Add(ReviewsErrors.PageSizeInvalid);

        return errors;
    }
}
