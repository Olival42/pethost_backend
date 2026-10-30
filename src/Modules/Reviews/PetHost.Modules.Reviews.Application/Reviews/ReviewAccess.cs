using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews;

/// <summary>Quem pede, pelo token: o lado (pelo papel) e o perfil da conta nesse lado.</summary>
public sealed record Reviewer(ReviewParty Side, Guid ProfileId);

/// <summary>Resolve quem pede a partir da conta e do papel do token.</summary>
internal static class ReviewAccess
{
    private static readonly Error UnsupportedRole = Error.Forbidden("Only owners and hosts can do this.");

    /// <summary>
    /// O perfil da conta <paramref name="userId"/> no papel <paramref name="role"/>. Sem o perfil:
    /// <c>404 REVIEW_OWNER_NOT_FOUND</c> / <c>REVIEW_HOST_NOT_FOUND</c>. Com
    /// <paramref name="requireAvailable"/>, inativo ou suspenso: <c>403 REVIEW_*_DEACTIVATED</c>.
    /// Outro papel: <c>403 FORBIDDEN</c>.
    /// </summary>
    public static async Task<Result<Reviewer>> ResolveAsync(
        IReviewParties reviewParties,
        Guid userId,
        string? role,
        bool requireAvailable,
        CancellationToken cancellationToken)
    {
        if (role == Roles.Owner)
        {
            var owner = await reviewParties.FindOwnerByAccountAsync(userId, cancellationToken).ConfigureAwait(false);
            return Check(owner, ReviewParty.Owner, ReviewsErrors.OwnerNotFound, ReviewsErrors.OwnerDeactivated, requireAvailable);
        }

        if (role == Roles.Host)
        {
            var host = await reviewParties.FindHostByAccountAsync(userId, cancellationToken).ConfigureAwait(false);
            return Check(host, ReviewParty.Host, ReviewsErrors.HostNotFound, ReviewsErrors.HostDeactivated, requireAvailable);
        }

        return Result<Reviewer>.Failure(UnsupportedRole);
    }

    private static Result<Reviewer> Check(
        ReviewerProfile? profile,
        ReviewParty side,
        Error notFound,
        Error deactivated,
        bool requireAvailable)
    {
        if (profile is null)
            return Result<Reviewer>.Failure(notFound);

        if (requireAvailable && !profile.IsAvailable)
            return Result<Reviewer>.Failure(deactivated);

        return Result<Reviewer>.Success(new Reviewer(side, profile.Id));
    }
}
