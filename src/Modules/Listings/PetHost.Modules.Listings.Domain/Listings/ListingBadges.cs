using System.Globalization;
using System.Text;

namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// Selos automáticos do cantinho (escopo, regra 10): calculados, nunca cadastrados. Saem no
/// cartão da busca e no detalhe, na ordem abaixo.
/// </summary>
/// <remarks>
/// "Bem avaliado" (nota 4,5+ com 3+ avaliações) ainda não sai: depende do módulo de avaliações.
/// </remarks>
public static class ListingBadges
{
    /// <summary>Vizinho: o cantinho fica no mesmo bairro (e cidade) do endereço de quem busca.</summary>
    public const string Neighbor = "neighbor";

    /// <summary>Responde rápido: o anfitrião responde aos pedidos em menos de 2h, em média.</summary>
    public const string FastResponder = "fast_responder";

    /// <summary>Casa com quintal: marcado no cantinho.</summary>
    public const string Yard = "yard";

    /// <summary>Experiente: o anfitrião tem 5 ou mais estadias concluídas.</summary>
    public const string Experienced = "experienced";

    /// <summary>Média de resposta abaixo disto ganha "Responde rápido".</summary>
    public static readonly TimeSpan FastResponseLimit = TimeSpan.FromHours(2);

    /// <summary>Estadias concluídas a partir das quais o anfitrião é "Experiente".</summary>
    public const int ExperiencedMinStays = 5;

    /// <summary>Todos os selos que existem, na ordem em que saem.</summary>
    public static IReadOnlyList<string> All { get; } = [Neighbor, FastResponder, Yard, Experienced];

    /// <summary>Os selos do cantinho para quem vê.</summary>
    /// <param name="viewerAddress">Endereço da conta de quem vê; sem endereço, não há "Vizinho".</param>
    /// <param name="completedStays">Estadias concluídas do anfitrião.</param>
    /// <param name="averageResponseTime">Tempo médio de resposta do anfitrião; nulo se nunca respondeu.</param>
    public static IReadOnlyList<string> For(
        Listing listing,
        ListingAddressData? viewerAddress,
        int completedStays,
        TimeSpan? averageResponseTime)
    {
        ArgumentNullException.ThrowIfNull(listing);

        var badges = new List<string>();

        if (viewerAddress is not null
            && SamePlace(viewerAddress.Neighborhood, listing.Address.Neighborhood)
            && SamePlace(viewerAddress.City, listing.Address.City))
        {
            badges.Add(Neighbor);
        }

        if (averageResponseTime < FastResponseLimit)
            badges.Add(FastResponder);

        if (listing.Details.HasYard)
            badges.Add(Yard);

        if (completedStays >= ExperiencedMinStays)
            badges.Add(Experienced);

        return badges;
    }

    /// <summary>Mesmo nome, sem diferença de acento, maiúsculas nem espaços nas pontas ("Zona 7" = "zona 7").</summary>
    private static bool SamePlace(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string text)
    {
        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
