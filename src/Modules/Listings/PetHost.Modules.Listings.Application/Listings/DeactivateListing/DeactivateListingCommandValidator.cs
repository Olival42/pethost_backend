using FluentValidation;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Listings.Application.Listings.DeactivateListing;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class DeactivateListingCommandValidator : AbstractValidator<DeactivateListingCommand>
{
    public DeactivateListingCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithError(ListingsErrors.ListingIdRequired);
    }
}
