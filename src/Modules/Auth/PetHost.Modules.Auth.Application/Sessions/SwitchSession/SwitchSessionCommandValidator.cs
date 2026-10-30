using FluentValidation;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Auth.Application.Sessions.SwitchSession;

public sealed class SwitchSessionCommandValidator : AbstractValidator<SwitchSessionCommand>
{
    public SwitchSessionCommandValidator()
    {
        RuleFor(x => x.Role)
            .Must(role => UserRoleValues.TryParse(role, out var parsed) && parsed is UserRole.Owner or UserRole.Host)
                .WithError(AuthErrors.RoleInvalid);

        RuleFor(x => x.Password)
            .NotEmpty().WithError(AuthErrors.PasswordRequired)
            .MaximumLength(Password.MaxLength)
                .WithError(AuthErrors.PasswordTooLong);
    }
}
