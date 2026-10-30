namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>
/// Uma das fotos da solicitação (frente, verso ou selfie). Faz parte do agregado
/// <see cref="HostApplication"/>: só muda por ele. Espelha <c>host.host_application_documents</c>.
/// </summary>
/// <remarks>
/// Guarda a <b>chave</b> do arquivo no bucket privado, nunca uma URL: ninguém abre o
/// documento por link. Quem lê é a API, numa rota do admin.
/// </remarks>
public sealed class HostApplicationDocument
{
    /// <summary>Limite de <c>storage_key</c>: <c>host-applications/&lt;guid de 32&gt;.webp</c> com folga.</summary>
    public const int StorageKeyMaxLength = 200;

    /// <summary>SHA-256 em hexadecimal.</summary>
    public const int ContentHashLength = 64;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private HostApplicationDocument()
    {
        StorageKey = null!;
        ContentHash = null!;
    }

    internal HostApplicationDocument(DocumentKind kind, string storageKey, string contentHash, long sizeBytes, DateTimeOffset now)
    {
        Id = HostApplicationDocumentId.New();
        Kind = kind;
        StorageKey = storageKey;
        ContentHash = contentHash;
        SizeBytes = sizeBytes;
        UploadedAt = now;
    }

    public HostApplicationDocumentId Id { get; private set; }

    public DocumentKind Kind { get; private set; }

    /// <summary>Chave do arquivo no bucket privado.</summary>
    public string StorageKey { get; private set; }

    /// <summary>SHA-256 da imagem: impede a mesma foto em duas vagas (frente e verso iguais).</summary>
    public string ContentHash { get; private set; }

    public long SizeBytes { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>Troca o arquivo desta vaga. Devolve a chave anterior, para apagar do bucket.</summary>
    internal string Replace(string storageKey, string contentHash, long sizeBytes, DateTimeOffset now)
    {
        var previous = StorageKey;

        StorageKey = storageKey;
        ContentHash = contentHash;
        SizeBytes = sizeBytes;
        UploadedAt = now;

        return previous;
    }
}
