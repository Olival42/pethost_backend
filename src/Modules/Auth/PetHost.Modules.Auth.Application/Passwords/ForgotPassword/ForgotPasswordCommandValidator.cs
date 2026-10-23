using FluentValidation;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Auth.Application.Passwords.ForgotPassword;

/// <summary>
/// Valida só o formato. Se a conta existe ou não é assunto do handler, que responde
/// igual nos dois casos.
/// </summary>
public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithError(AuthErrors.EmailRequired)
            .MaximumLength(Email.MaxLength)
                .WithError(AuthErrors.EmailTooLong);

        RuleFor(x => x.Role)
            .NotEmpty().WithError(AuthErrors.RoleRequired)
            .Must(role => UserRoleValues.TryParse(role, out _))
                .WithError(AuthErrors.RoleUnknown);
    }
}
