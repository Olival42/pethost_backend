using FluentValidation;

namespace PetHost.Modules.Listings.Application.Listings.ReplaceListingPhoto;

/// <summary>Só os ids. O arquivo é conferido pelo bucket, depois de saber que a foto é do cantinho de quem pede.</summary>
public sealed class ReplaceListingPhotoCommandValidator : AbstractValidator<ReplaceListingPhotoCommand>
{
    public ReplaceListingPhotoCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage("Listing id is required.");

        RuleFor(x => x.PhotoId)
            .NotEmpty().WithMessage("Photo id is required.");
    }
}
