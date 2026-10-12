using FluentValidation;

namespace PetHost.Modules.Listings.Application.Listings.UpdateListing;

/// <summary>
/// Só o que dá para conferir sem o cantinho atual. As regras dependem do valor que fica depois
/// de mesclar o PATCH e de o dono ser pessoa física ou empresa: quem as confere é o handler,
/// pelo domínio — com todos os erros juntos num só 400.
/// </summary>
public sealed class UpdateListingCommandValidator : AbstractValidator<UpdateListingCommand>
{
    public UpdateListingCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage("Listing id is required.");
    }
}
