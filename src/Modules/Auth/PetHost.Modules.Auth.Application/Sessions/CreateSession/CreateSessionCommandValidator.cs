using FluentValidation;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Sessions.CreateSession;

/// <summary>
/// Valida só o formato. Não diz se o e-mail existe nem se a senha está certa —
/// isso é resposta única e genérica do handler, para não virar oráculo de contas.
/// </summary>
public sealed class CreateSessionCommandValidator : AbstractValidator<CreateSessionCommand>
{
    public CreateSessionCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(Email.MaxLength)
                .WithMessage($"Email must be at most {Email.MaxLength} characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(PasswordPolicy.MaxLength)
                .WithMessage($"Password must be at most {PasswordPolicy.MaxLength} characters.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(role => UserRoleValues.TryParse(role, out _))
                .WithMessage("Role must be one of 'owner', 'host' or 'admin'.");
    }
}
