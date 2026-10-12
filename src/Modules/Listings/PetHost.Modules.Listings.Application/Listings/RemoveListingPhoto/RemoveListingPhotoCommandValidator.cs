using FluentValidation;

namespace PetHost.Modules.Listings.Application.Listings.RemoveListingPhoto;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class RemoveListingPhotoCommandValidator : AbstractValidator<RemoveListingPhotoCommand>
{
    public RemoveListingPhotoCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage("Listing id is required.");

        RuleFor(x => x.PhotoId)
            .NotEmpty().WithMessage("Photo id is required.");
    }
}
