namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Uma foto já gravada no bucket privado, pronta para entrar na sua vaga.</summary>
/// <param name="StorageKey">Chave do arquivo no bucket privado (nunca uma URL).</param>
/// <param name="ContentHash">SHA-256 do conteúdo: a mesma imagem não vale para duas vagas.</param>
public sealed record NewHostApplicationDocument(DocumentKind Kind, string StorageKey, string ContentHash, long SizeBytes);
