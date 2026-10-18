using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>
/// Impressão do CPF para o bloqueio de identidade: o mesmo CPF dá sempre a mesma impressão, e sem a
/// chave da configuração não dá para voltar ao CPF (nem testar CPFs um a um). A Infrastructure
/// implementa com HMAC-SHA256.
/// </summary>
public interface ICpfFingerprint
{
    /// <summary>64 caracteres hexadecimais minúsculos.</summary>
    string Of(Cpf cpf);
}
