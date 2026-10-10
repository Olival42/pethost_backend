using FluentValidation;

namespace PetHost.Modules.Pets.Application.Pets.ReplacePetPhoto;

/// <summary>
/// Só os ids. O arquivo é conferido pelo bucket, depois de saber que o pet é de quem pede e
/// que a foto é dele.
/// </summary>
public sealed class ReplacePetPhotoCommandValidator : AbstractValidator<ReplacePetPhotoCommand>
{
    public ReplacePetPhotoCommandValidator()
    {
        RuleFor(x => x.PetId)
            .NotEmpty().WithMessage("Pet id is required.");

        RuleFor(x => x.PhotoId)
            .NotEmpty().WithMessage("Photo id is required.");
    }
}
