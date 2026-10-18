using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure.Verification;

/// <summary>
/// <see cref="ICpfFingerprint"/> com HMAC-SHA256 e a chave <c>HostVerification:CpfFingerprintKey</c>.
/// Sem a chave, a impressão não volta ao CPF: um SHA-256 simples cairia testando os 10⁹ CPFs possíveis.
/// </summary>
/// <remarks>Trocar a chave invalida os bloqueios já gravados (eles param de casar com os CPFs).</remarks>
internal sealed class HmacCpfFingerprint(IOptions<HostVerificationOptions> options) : ICpfFingerprint
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.CpfFingerprintKey);

    public string Of(Cpf cpf)
    {
        ArgumentNullException.ThrowIfNull(cpf);

        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(cpf.Value)));
    }
}
