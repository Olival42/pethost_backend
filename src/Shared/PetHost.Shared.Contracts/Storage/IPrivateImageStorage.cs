using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Contracts.Storage;

/// <summary>
/// Bucket <b>privado</b> para imagens que ninguém pode abrir por link: documentos de
/// identidade e selfies da verificação do anfitrião. Separado do bucket público de
/// <see cref="IImageStorage"/> (§5).
/// </summary>
/// <remarks>
/// Aqui não existe URL pública: o banco guarda a <b>chave</b> do arquivo, e só a API lê o
/// conteúdo (com as credenciais do servidor) para repassar a quem tem permissão — o admin,
/// numa rota auditada. As regras do arquivo são as mesmas das fotos públicas
/// (<see cref="ImageRules"/>, erros de <see cref="ImageErrors"/>).
/// </remarks>
public interface IPrivateImageStorage
{
    /// <summary>
    /// Confere e grava a imagem em <paramref name="folder"/>. Erros de arquivo voltam como
    /// <c>VALIDATION_ERROR</c> no campo <c>file</c>; nada é gravado.
    /// </summary>
    /// <param name="folder">Pasta no bucket privado. Use <see cref="PrivateImageFolders"/>.</param>
    Task<Result<StoredPrivateImage>> SaveAsync(ImageUpload? image, string folder, CancellationToken cancellationToken);

    /// <summary>
    /// Abre a imagem para leitura, ou <c>null</c> se ela não existe mais (já apagada). Quem
    /// recebe é dono do stream.
    /// </summary>
    Task<PrivateImageContent?> OpenAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Apaga a imagem dessa chave. Ignora <c>null</c>. Nunca lança: falha vai para o log — um
    /// arquivo órfão não pode derrubar o pedido.
    /// </summary>
    Task DeleteAsync(string? key, CancellationToken cancellationToken);
}

/// <summary>Imagem gravada no bucket privado.</summary>
/// <param name="Key">Chave do arquivo no bucket: é o que vai para o banco. Nunca uma URL.</param>
/// <param name="ContentHash">SHA-256 dos bytes, em hexadecimal minúsculo (64 caracteres).</param>
/// <param name="SizeBytes">Tamanho do arquivo.</param>
public sealed record StoredPrivateImage(string Key, string ContentHash, long SizeBytes);

/// <summary>O conteúdo de uma imagem privada, pronto para repassar.</summary>
/// <param name="Content">Os bytes. Quem recebe fecha o stream.</param>
/// <param name="ContentType">O formato real, gravado no upload (ex.: <c>image/jpeg</c>).</param>
public sealed record PrivateImageContent(Stream Content, string ContentType);

/// <summary>Pastas do bucket privado.</summary>
public static class PrivateImageFolders
{
    /// <summary>Documentos (frente e verso) e selfies das solicitações de anfitrião.</summary>
    public const string HostApplications = "host-applications";
}
