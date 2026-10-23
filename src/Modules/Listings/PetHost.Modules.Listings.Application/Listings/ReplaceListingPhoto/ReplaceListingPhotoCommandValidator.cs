using FluentValidation;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Listings.Application.Listings.ReplaceListingPhoto;

/// <summary>Só os ids. O arquivo é conferido pelo bucket, depois de saber que a foto é do cantinho de quem pede.</summary>
public sealed class ReplaceListingPhotoCommandValidator : AbstractValidator<ReplaceListingPhotoCommand>
{
    public ReplaceListingPhotoCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithError(ListingsErrors.ListingIdRequired);

        RuleFor(x => x.PhotoId)
            .NotEmpty().WithError(ListingsErrors.PhotoIdRequired);
    }
}
