using FluentValidation;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Listings.Application.Listings.AddListingPhoto;

/// <summary>
/// Só o id. O arquivo (faltando, grande, formato) é conferido pelo bucket, depois de saber que o
/// cantinho é de quem pede: um estranho não chega a enviar nada.
/// </summary>
public sealed class AddListingPhotoCommandValidator : AbstractValidator<AddListingPhotoCommand>
{
    public AddListingPhotoCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithError(ListingsErrors.ListingIdRequired);
    }
}
