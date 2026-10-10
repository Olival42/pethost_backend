using FluentValidation;

namespace PetHost.Modules.Pets.Application.Pets.AddPetPhoto;

/// <summary>
/// Só o id. O arquivo (faltando, grande, formato) é conferido pelo bucket, depois de saber
/// que o pet é de quem pede: um estranho não chega a enviar nada.
/// </summary>
public sealed class AddPetPhotoCommandValidator : AbstractValidator<AddPetPhotoCommand>
{
    public AddPetPhotoCommandValidator()
    {
        RuleFor(x => x.PetId)
            .NotEmpty().WithMessage("Pet id is required.");
    }
}
