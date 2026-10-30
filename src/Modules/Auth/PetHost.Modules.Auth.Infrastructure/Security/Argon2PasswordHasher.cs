using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using PetHost.Modules.Auth.Application.Abstractions;

namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Hash de senha com Argon2id, serializado no formato PHC:
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>.
/// </summary>
/// <remarks>
/// O formato guarda algoritmo, versão, custo e salt junto do hash. Isso permite
/// subir o custo no futuro sem invalidar as senhas já cadastradas: cada hash é
/// conferido com os parâmetros com que foi gerado, lidos da própria string.
/// </remarks>
internal sealed class Argon2PasswordHasher(IOptions<Argon2Options> options) : IPasswordHasher
{
    private const string Algorithm = "argon2id";
    private const int Version = 19;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    private readonly Argon2Options _options = options.Value;

    /// <summary>
    /// Calcula o hash. Senha vazia e violacao da regra de <c>Password</c> e nunca
    /// chega aqui pelo caminho normal (o validador barra antes): falha alto em
    /// vez de gravar o hash de uma senha vazia.
    /// </summary>
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);

        var hash = Compute(
            password,
            salt,
            _options.MemorySizeKib,
            _options.Iterations,
            _options.DegreeOfParallelism,
            HashSizeBytes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"${Algorithm}$v={Version}$m={_options.MemorySizeKib},t={_options.Iterations},p={_options.DegreeOfParallelism}${ToBase64(salt)}${ToBase64(hash)}");
    }

    public bool Verify(string password, string hash)
    {
        // Senha vazia nao autentica: a politica exige 8 caracteres, entao nenhum
        // hash guardado veio de uma senha vazia. Tratar aqui evita que uma
        // requisicao sem senha virasse 500 em vez de 401.
        if (string.IsNullOrEmpty(password) || !TryParse(hash, out var parsed))
            return false;

        var computed = Compute(
            password,
            parsed.Salt,
            parsed.MemorySizeKib,
            parsed.Iterations,
            parsed.DegreeOfParallelism,
            parsed.Hash.Length);

        // Comparação em tempo constante: um `==` vazaria o número de bytes
        // corretos pelo tempo de retorno.
        return CryptographicOperations.FixedTimeEquals(computed, parsed.Hash);
    }

    private static byte[] Compute(
        string password,
        byte[] salt,
        int memorySizeKib,
        int iterations,
        int degreeOfParallelism,
        int hashSize)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memorySizeKib,
            Iterations = iterations,
            DegreeOfParallelism = degreeOfParallelism,
        };

        return argon2.GetBytes(hashSize);
    }

    private static bool TryParse(string? encoded, out ParsedHash parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(encoded))
            return false;

        // ["", "argon2id", "v=19", "m=19456,t=2,p=1", salt, hash]
        var parts = encoded.Split('$');
        if (parts.Length != 6 || parts[1] != Algorithm)
            return false;

        if (!TryReadLabeled(parts[2], "v", out var version) || version != Version)
            return false;

        var cost = parts[3].Split(',');
        if (cost.Length != 3
            || !TryReadLabeled(cost[0], "m", out var memory)
            || !TryReadLabeled(cost[1], "t", out var iterations)
            || !TryReadLabeled(cost[2], "p", out var parallelism))
        {
            return false;
        }

        if (!TryFromBase64(parts[4], out var salt) || !TryFromBase64(parts[5], out var hash))
            return false;

        parsed = new ParsedHash(salt, hash, memory, iterations, parallelism);
        return true;
    }

    private static bool TryReadLabeled(string segment, string label, out int value)
    {
        value = 0;

        var separator = segment.IndexOf('=', StringComparison.Ordinal);
        if (separator < 0 || !segment.AsSpan(0, separator).SequenceEqual(label))
            return false;

        return int.TryParse(
            segment.AsSpan(separator + 1),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value);
    }

    /// <summary>Base64 sem o preenchimento <c>=</c>, como manda o formato PHC.</summary>
    private static string ToBase64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        string? padded = (value.Length % 4) switch
        {
            2 => value + "==",
            3 => value + "=",
            0 => value,
            _ => null,
        };

        if (padded is null)
        {
            bytes = [];
            return false;
        }

        var buffer = new byte[padded.Length / 4 * 3];

        if (Convert.TryFromBase64String(padded, buffer, out var written))
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = [];
        return false;
    }

    private readonly record struct ParsedHash(
        byte[] Salt,
        byte[] Hash,
        int MemorySizeKib,
        int Iterations,
        int DegreeOfParallelism);
}
