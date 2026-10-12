namespace PetHost.Modules.Listings.Infrastructure.Geocoding;

/// <summary>
/// Configuração do geocodificador (seção <c>Geocoding</c>). O padrão é o Nominatim público do
/// OpenStreetMap: gratuito, sem chave, mas com política de uso — identificar a aplicação no
/// <c>User-Agent</c> e no máximo 1 pedido por segundo.
/// </summary>
public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    /// <summary>Endereço do Nominatim. Troque por uma instância própria se o volume crescer.</summary>
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    /// <summary>Identifica a aplicação (obrigatório pela política do Nominatim).</summary>
    public string UserAgent { get; set; } = "PetHost/1.0 (projeto academico)";

    /// <summary>E-mail de contato enviado ao Nominatim (opcional, recomendado pela política).</summary>
    public string? Email { get; set; }

    /// <summary>Tempo máximo de cada pedido.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Intervalo mínimo entre dois pedidos ao serviço (política: 1 por segundo).</summary>
    public int MinIntervalMilliseconds { get; set; } = 1100;

    /// <summary>Por quanto tempo um endereço achado fica em cache.</summary>
    public int CacheHours { get; set; } = 24 * 7;

    /// <summary>Por quanto tempo um "não achou" fica em cache (o endereço pode ser corrigido).</summary>
    public int NotFoundCacheMinutes { get; set; } = 60;
}
