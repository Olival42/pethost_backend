using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Infrastructure.Http;

/// <summary>Ponte com o ASP.NET Core: o controller só chama <c>ToActionResult()</c> (§7).</summary>
public static class ResultActionResultExtensions
{
    public static IActionResult ToActionResult<T>(
        this Result<T> result,
        int successStatusCode = StatusCodes.Status200OK)
    {
        var response = result.ToApiResponse();

        return new ObjectResult(response)
        {
            StatusCode = response.Success ? successStatusCode : response.Error!.Code.ToHttpStatusCode(),
        };
    }

    /// <summary>
    /// <c>201 Created</c> com o header <c>Location</c> apontando para onde o recurso
    /// criado pode ser lido (§13). Em falha, igual a <see cref="ToActionResult{T}"/>.
    /// </summary>
    public static IActionResult ToCreatedResult<T>(this Result<T> result, string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        if (result.IsFailure)
            return result.ToActionResult();

        return new CreatedResult(location, result.ToApiResponse());
    }
}
