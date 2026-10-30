using FluentValidation;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Listings.Application.Listings.RemoveListingPhoto;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class RemoveListingPhotoCommandValidator : AbstractValidator<RemoveListingPhotoCommand>
{
    public RemoveListingPhotoCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithError(ListingsErrors.ListingIdRequired);

        RuleFor(x => x.PhotoId)
            .NotEmpty().WithError(ListingsErrors.PhotoIdRequired);
    }
}
