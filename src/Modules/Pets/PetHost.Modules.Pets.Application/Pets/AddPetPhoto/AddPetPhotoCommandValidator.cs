using FluentValidation;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

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
            .NotEmpty().WithError(PetsErrors.PetIdRequired);
    }
}
