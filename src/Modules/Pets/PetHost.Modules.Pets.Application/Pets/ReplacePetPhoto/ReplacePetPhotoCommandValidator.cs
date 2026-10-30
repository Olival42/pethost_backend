using FluentValidation;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

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
            .NotEmpty().WithError(PetsErrors.PetIdRequired);

        RuleFor(x => x.PhotoId)
            .NotEmpty().WithError(PetsErrors.PhotoIdRequired);
    }
}
