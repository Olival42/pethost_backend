using FluentValidation;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Pets.Application.Pets.RemovePetPhoto;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class RemovePetPhotoCommandValidator : AbstractValidator<RemovePetPhotoCommand>
{
    public RemovePetPhotoCommandValidator()
    {
        RuleFor(x => x.PetId)
            .NotEmpty().WithError(PetsErrors.PetIdRequired);

        RuleFor(x => x.PhotoId)
            .NotEmpty().WithError(PetsErrors.PhotoIdRequired);
    }
}
