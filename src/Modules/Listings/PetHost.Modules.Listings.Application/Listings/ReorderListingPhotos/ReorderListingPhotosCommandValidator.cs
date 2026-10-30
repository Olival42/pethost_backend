using FluentValidation;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Listings.Application.Listings.ReorderListingPhotos;

/// <summary>
/// O formato da lista: veio, sem id vazio, sem repetição e com no máximo 5. Se ela tem
/// exatamente as fotos do cantinho, quem confere é o domínio.
/// </summary>
public sealed class ReorderListingPhotosCommandValidator : AbstractValidator<ReorderListingPhotosCommand>
{
    public ReorderListingPhotosCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithError(ListingsErrors.ListingIdRequired);

        RuleFor(x => x.PhotoIds)
            .NotNull().WithError(ListingsErrors.PhotoOrderInvalid)
            .Must(ids => ids is null || (ids.Count <= Listing.MaxPhotos && !ids.Contains(Guid.Empty) && ids.Distinct().Count() == ids.Count))
            .WithError(ListingsErrors.PhotoOrderInvalid);
    }
}
