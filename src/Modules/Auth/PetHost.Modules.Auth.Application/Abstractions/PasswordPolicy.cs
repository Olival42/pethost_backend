namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>Limites da senha em texto, aplicados pelos validadores.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>
    /// Teto para não transformar o hash em vetor de negação de serviço: o custo do
    /// Argon2 cresce com o tamanho da entrada.
    /// </summary>
    public const int MaxLength = 128;
}
