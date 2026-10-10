using FluentValidation;

namespace PetHost.Modules.Pets.Application.Pets.RemovePetPhoto;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class RemovePetPhotoCommandValidator : AbstractValidator<RemovePetPhotoCommand>
{
    public RemovePetPhotoCommandValidator()
    {
        RuleFor(x => x.PetId)
            .NotEmpty().WithMessage("Pet id is required.");

        RuleFor(x => x.PhotoId)
            .NotEmpty().WithMessage("Photo id is required.");
    }
}
