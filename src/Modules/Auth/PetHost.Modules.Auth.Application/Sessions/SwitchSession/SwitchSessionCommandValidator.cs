using FluentValidation;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Sessions.SwitchSession;

public sealed class SwitchSessionCommandValidator : AbstractValidator<SwitchSessionCommand>
{
    public SwitchSessionCommandValidator()
    {
        RuleFor(x => x.Role)
            .Must(role => UserRoleValues.TryParse(role, out var parsed) && parsed is UserRole.Owner or UserRole.Host)
                .WithMessage("Role must be either 'owner' or 'host'.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(Password.MaxLength)
                .WithMessage($"Password must be at most {Password.MaxLength} characters.");
    }
}
