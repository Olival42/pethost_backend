using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Contracts.Storage;

/// <summary>
/// Onde as imagens ficam: um bucket compatível com S3 (Cloudflare R2 em produção, RustFS em
/// desenvolvimento). Contrato único para qualquer módulo que tenha foto — avatar do tutor,
/// foto do pet, do anfitrião, do cantinho (§5).
/// </summary>
/// <remarks>
/// O bucket é público para leitura: a URL devolvida vai direto para o banco e para o front.
/// O nome do arquivo é aleatório (ninguém adivinha o link) e nunca é reaproveitado — trocar a
/// foto gera um arquivo novo, então o cache do CDN pode ser eterno. Para o fluxo completo de
/// trocar ou remover uma foto (enviar, gravar, apagar a antiga), use <see cref="ImageReplacement"/>.
/// </remarks>
public interface IImageStorage
{
    /// <summary>
    /// Confere e grava a imagem em <paramref name="folder"/>. Erros de arquivo (faltando, vazio,
    /// grande demais, formato não aceito) voltam como <c>VALIDATION_ERROR</c> no campo
    /// <c>file</c>; nada é gravado.
    /// </summary>
    /// <param name="folder">Pasta no bucket. Use <see cref="ImageFolders"/>.</param>
    Task<Result<StoredImage>> SaveAsync(ImageUpload? image, string folder, CancellationToken cancellationToken);

    /// <summary>
    /// Apaga a imagem dessa URL. Ignora <c>null</c> e URL que não é do nosso bucket (foto antiga
    /// externa). Nunca lança: falha vai para o log — um arquivo órfão não pode derrubar o pedido.
    /// </summary>
    Task DeleteAsync(string? url, CancellationToken cancellationToken);
}

/// <summary>A imagem como chegou do cliente. Quem cria é dono do stream.</summary>
/// <param name="Content">Bytes da imagem. O formato real é conferido pelo conteúdo, não pelo nome.</param>
/// <param name="FileName">Nome original. Só informativo: não vai para o bucket.</param>
/// <param name="ContentType">O que o cliente declarou. Só informativo, pelo mesmo motivo.</param>
public sealed record ImageUpload(Stream Content, string? FileName, string? ContentType);

/// <summary>Imagem gravada.</summary>
/// <param name="Url">URL pública absoluta: é o que vai para o banco.</param>
/// <param name="ContentHash">
/// SHA-256 dos bytes da imagem, em hexadecimal minúsculo (64 caracteres). A mesma imagem dá
/// sempre o mesmo hash: serve para recusar foto repetida.
/// </param>
public sealed record StoredImage(string Url, string ContentHash);

/// <summary>Pastas do bucket, uma por tipo de dono da imagem.</summary>
public static class ImageFolders
{
    /// <summary>Foto de perfil da conta (tutor e anfitrião).</summary>
    public const string Avatars = "avatars";

    public const string Pets = "pets";

    /// <summary>Reservada: fotos do anfitrião.</summary>
    public const string Hosts = "hosts";

    /// <summary>Reservada: fotos do cantinho (anúncio).</summary>
    public const string Listings = "listings";
}

/// <summary>O que é aceito como imagem.</summary>
public static class ImageRules
{
    /// <summary>Tamanho máximo do arquivo: 5 MB.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Teto do corpo do pedido de upload: a imagem mais a moldura do multipart. Acima disso o
    /// servidor recusa sem ler (413); entre isso e <see cref="MaxBytes"/>, a resposta é o 400
    /// com a mensagem de tamanho.
    /// </summary>
    public const long MaxRequestBytes = 8 * 1024 * 1024;

    /// <summary>Formatos aceitos, para mensagens e documentação.</summary>
    public static readonly IReadOnlyList<string> AcceptedFormats = ["JPG/JPEG", "PNG", "WebP"];
}
