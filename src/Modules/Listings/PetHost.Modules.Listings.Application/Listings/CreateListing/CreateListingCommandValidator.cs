using FluentValidation;

namespace PetHost.Modules.Listings.Application.Listings.CreateListing;

/// <summary>
/// Só o que não depende do anfitrião. As regras do cantinho mudam conforme ele é pessoa física
/// ou empresa (tipo da casa, teto da capacidade), e o validador não consulta outro módulo (§10):
/// quem as confere é o handler, pelo domínio — com todos os erros juntos num só 400.
/// </summary>
public sealed class CreateListingCommandValidator : AbstractValidator<CreateListingCommand>
{
    public CreateListingCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");
    }
}
