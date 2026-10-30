namespace PetHost.Api.Seed;

/// <summary>
/// Dados de teste (<see cref="TestDataCatalog"/>): tutores com pets e anfitriões com cantinho,
/// criados na subida. Desligado por padrão. Vem do ambiente como <c>Seed__TestData__Enabled</c>
/// e <c>Seed__TestData__Password</c>.
/// </summary>
/// <remarks>Só dados fictícios: e-mails <c>@pethost.local</c>, CPFs inventados (com dígitos válidos).</remarks>
public sealed class TestDataSeedOptions
{
    public const string SectionName = "Seed:TestData";

    public bool Enabled { get; init; }

    /// <summary>A mesma senha para todas as contas de teste. Precisa seguir a regra de senha forte do cadastro.</summary>
    public string? Password { get; init; }
}
