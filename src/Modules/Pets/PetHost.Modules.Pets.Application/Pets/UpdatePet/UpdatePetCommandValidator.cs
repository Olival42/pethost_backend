using FluentValidation;

namespace PetHost.Modules.Pets.Application.Pets.UpdatePet;

/// <summary>
/// Só o que dá para conferir sem a ficha atual. As regras da ficha dependem do valor que
/// fica depois de mesclar o PATCH (porte só para cachorro e gato, descrição só para exótico), então
/// quem as confere é o handler, pelo domínio — com todos os erros juntos num só 400.
/// </summary>
public sealed class UpdatePetCommandValidator : AbstractValidator<UpdatePetCommand>
{
    public UpdatePetCommandValidator()
    {
        RuleFor(x => x.PetId)
            .NotEmpty().WithMessage("Pet id is required.");
    }
}
