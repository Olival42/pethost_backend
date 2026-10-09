using FluentValidation;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>Regras de senha para os validadores de entrada.</summary>
public static class PasswordRules
{
    /// <summary>
    /// Senha forte, pela regra do value object <see cref="Password"/> — uma fonte só,
    /// o validador não repete a regra. Cada regra que falha vira uma mensagem no campo
    /// validado, então o cliente recebe a lista do que falta.
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, string?> StrongPassword<T>(this IRuleBuilder<T, string?> ruleBuilder) =>
        ruleBuilder.MustBeValid(Password.Create);
}
