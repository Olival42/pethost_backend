namespace PetHost.Modules.Auth.Infrastructure.Persistence.Seed;

/// <summary>
/// Credenciais do admin inicial. O dicionário de dados diz que o admin é criado
/// direto no banco (seed) — não existe cadastro de admin pela API.
/// </summary>
/// <remarks>
/// Vem do ambiente como <c>Seed__Admin__Enabled</c>, <c>Seed__Admin__Email</c>,
/// <c>Seed__Admin__Password</c> e <c>Seed__Admin__FullName</c>. A senha nunca é
/// versionada nem registrada em log.
/// </remarks>
public sealed class AdminSeedOptions
{
    public const string SectionName = "Seed:Admin";

    /// <summary>Desligado por padrão: ligar é decisão explícita do ambiente.</summary>
    public bool Enabled { get; init; }

    public string FullName { get; init; } = "PetHost Admin";

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
