namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Custo do Argon2id. Os valores default são o mínimo recomendado pelo OWASP
/// Password Storage Cheat Sheet para Argon2id: 19 MiB de memória, 2 iterações,
/// 1 grau de paralelismo.
/// </summary>
/// <remarks>
/// Subir o custo é seguro: o hash guarda os próprios parâmetros, então senhas
/// antigas continuam conferindo com o custo com que foram criadas.
/// </remarks>
public sealed class Argon2Options
{
    public const string SectionName = "Argon2";

    /// <summary>Memória em KiB.</summary>
    public int MemorySizeKib { get; init; } = 19456;

    public int Iterations { get; init; } = 2;

    public int DegreeOfParallelism { get; init; } = 1;
}
