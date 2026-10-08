namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>
/// Porta de hash de senha. A Application não sabe qual algoritmo é usado;
/// a Infrastructure implementa com Argon2id.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Calcula o hash de uma senha em texto. O resultado inclui o salt.</summary>
    string Hash(string password);

    /// <summary>
    /// Confere a senha contra o hash. Implementação obrigatoriamente em tempo
    /// constante, para não vazar informação por tempo de resposta.
    /// </summary>
    bool Verify(string password, string hash);
}
