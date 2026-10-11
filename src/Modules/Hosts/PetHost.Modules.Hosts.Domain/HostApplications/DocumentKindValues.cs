namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Valores de <see cref="DocumentKind"/> no banco, no JSON e na rota de upload.</summary>
public static class DocumentKindValues
{
    private static readonly WireValues<DocumentKind> Values = new(new Dictionary<DocumentKind, string>
    {
        [DocumentKind.Front] = "front",
        [DocumentKind.Back] = "back",
        [DocumentKind.Selfie] = "selfie",
    });

    /// <summary>As três fotos, todas obrigatórias para enviar a solicitação.</summary>
    public static IReadOnlyList<DocumentKind> Required { get; } = [DocumentKind.Front, DocumentKind.Back, DocumentKind.Selfie];

    public static IReadOnlyList<string> All => Values.All;

    public static string ToWire(DocumentKind kind) => Values.ToWire(kind);

    public static DocumentKind FromWire(string value) => Values.FromWire(value);

    public static bool TryParse(string? value, out DocumentKind kind) => Values.TryParse(value, out kind);
}
