using FluentValidation;
using PetHost.Modules.Auth.Domain.Users;

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
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(Email.MaxLength)
                .WithMessage($"Email must be at most {Email.MaxLength} characters.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(role => UserRoleValues.TryParse(role, out _))
                .WithMessage("Role must be one of 'owner', 'host' or 'admin'.");
    }
}
