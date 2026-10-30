namespace PetHost.Modules.Listings.Presentation.Listings;

/// <summary>Query string de <c>GET /api/v1/listings</c>. Tudo opcional; os filtros se somam.</summary>
public sealed class ListingSearchRequest
{
    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    /// <summary>De 5 a 50 km. Na busca por raio, padrão 10.</summary>
    public int? RadiusKm { get; init; }

    /// <summary>UF, ex.: <c>PR</c>.</summary>
    public string? State { get; init; }

    public string? City { get; init; }

    public string? Neighborhood { get; init; }

    /// <summary>Espécies que o cantinho precisa aceitar (todas).</summary>
    public IReadOnlyList<string>? Species { get; init; }

    /// <summary>Portes que o cantinho precisa aceitar (todos): <c>small</c>, <c>medium</c>, <c>large</c>.</summary>
    public IReadOnlyList<string>? Sizes { get; init; }

    /// <summary>Dias em que o cantinho precisa receber (todos).</summary>
    public IReadOnlyList<string>? Weekdays { get; init; }

    public decimal? MinNightlyRate { get; init; }

    public decimal? MaxNightlyRate { get; init; }

    public int? MinCapacity { get; init; }

    public bool? HasYard { get; init; }

    /// <summary><c>house</c> ou <c>apartment</c>.</summary>
    public string? HomeType { get; init; }

    /// <summary>Se na casa mora cachorro.</summary>
    public bool? HasDogs { get; init; }

    /// <summary>Se na casa mora gato.</summary>
    public bool? HasCats { get; init; }

    /// <summary>Se na casa mora criança.</summary>
    public bool? HasKids { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    /// <summary>Chegada (<c>yyyy-MM-dd</c>), junto com <see cref="CheckOut"/>: só cantinhos com vaga e que recebem em todos os dias.</summary>
    public string? CheckIn { get; init; }

    /// <summary>Saída (<c>yyyy-MM-dd</c>).</summary>
    public string? CheckOut { get; init; }

    /// <summary>Quantos pets vão (padrão 1): a vaga precisa caber todos.</summary>
    public int? Pets { get; init; }
}
