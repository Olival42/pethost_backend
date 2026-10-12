using FluentValidation;

namespace PetHost.Modules.Listings.Application.Listings.DeactivateListing;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class DeactivateListingCommandValidator : AbstractValidator<DeactivateListingCommand>
{
    public DeactivateListingCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage("Listing id is required.");
    }
}
