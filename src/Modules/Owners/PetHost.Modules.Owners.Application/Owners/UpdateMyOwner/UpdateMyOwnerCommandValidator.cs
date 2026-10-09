using FluentValidation;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Owners.Application.Owners.UpdateMyOwner;

/// <summary>
/// Valida o patch inteiro de uma vez: o CPF aqui (se veio) e os dados da conta pela
/// porta do Auth, contra o perfil atual. Todos os erros voltam juntos num só 400.
/// </summary>
public sealed class UpdateMyOwnerCommandValidator : AbstractValidator<UpdateMyOwnerCommand>
{
    public UpdateMyOwnerCommandValidator(IOwnerAccounts ownerAccounts)
    {
        ArgumentNullException.ThrowIfNull(ownerAccounts);

        RuleFor(x => x.Cpf)
            .Must(cpf => Cpf.Create(cpf).IsSuccess)
                .WithMessage(OwnersErrors.CpfInvalid.Message)
            .When(x => x.Cpf is not null);

        RuleFor(x => x).CustomAsync(async (command, context, cancellationToken) =>
        {
            var patch = command.ToProfilePatch();
            if (patch.IsEmpty)
                return;

            var account = await ownerAccounts
                .ValidateProfileAsync(command.UserId, patch, cancellationToken)
                .ConfigureAwait(false);

            if (account.IsSuccess)
                return;

            // Só erros de formato; conta inexistente fica com o handler.
            foreach (var error in account.Errors!.Where(e => e.Code == ErrorCodes.Validation))
                context.AddFailure(error.Field ?? string.Empty, error.Message);
        });
    }
}
