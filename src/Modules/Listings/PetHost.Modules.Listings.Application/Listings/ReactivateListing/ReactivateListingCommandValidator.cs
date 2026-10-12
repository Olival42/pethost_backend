using FluentValidation;

namespace PetHost.Modules.Listings.Application.Listings.ReactivateListing;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class ReactivateListingCommandValidator : AbstractValidator<ReactivateListingCommand>
{
    public ReactivateListingCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage("Listing id is required.");
    }
}
