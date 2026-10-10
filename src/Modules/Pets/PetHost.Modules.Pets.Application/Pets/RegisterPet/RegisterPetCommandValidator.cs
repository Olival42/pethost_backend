using FluentValidation;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Application.Pets.RegisterPet;

/// <summary>
/// Valida a ficha inteira de uma vez, pela regra do próprio domínio (<see cref="PetProfile"/>),
/// em vez de repeti-la. Todos os erros voltam juntos num só 400, cada um no seu campo.
/// </summary>
public sealed class RegisterPetCommandValidator : AbstractValidator<RegisterPetCommand>
{
    public RegisterPetCommandValidator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        RuleFor(x => x).Custom((command, context) =>
        {
            var profile = PetProfile.Create(command.ToProfileData(), timeProvider.GetUtcNow());
            if (profile.IsSuccess)
                return;

            foreach (var error in profile.Errors!)
                context.AddFailure(error.Field ?? string.Empty, error.Message);
        });
    }
}
