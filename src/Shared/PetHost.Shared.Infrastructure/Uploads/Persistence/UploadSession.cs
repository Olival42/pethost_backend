namespace PetHost.Shared.Infrastructure.Uploads.Persistence;

/// <summary>
/// Um upload direto ao bucket (<c>uploads.upload_sessions</c>), do pedido da URL assinada até a
/// confirmação. O arquivo fica na área de espera do bucket <b>privado</b>
/// (<see cref="StagingKey"/>) até ser conferido e aplicado ao recurso.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="UploadStatus.Pending"/>: URL emitida, esperando o arquivo e a confirmação.</item>
/// <item><see cref="UploadStatus.Processing"/>: uma confirmação pegou o upload (reserva até
/// <see cref="LeaseUntil"/>). Se a API cair no meio, a reserva vence e dá para confirmar de novo.</item>
/// <item><see cref="UploadStatus.Completed"/>: aplicado; <see cref="ResultJson"/> guarda a resposta,
/// devolvida igual a quem confirmar de novo.</item>
/// <item><see cref="UploadStatus.Rejected"/>: o arquivo ou a regra de negócio recusou; o arquivo foi apagado.</item>
/// <item><see cref="UploadStatus.Expired"/>: passou de <see cref="ConfirmExpiresAt"/> sem confirmar; o arquivo foi apagado.</item>
/// </list>
/// </remarks>
public sealed class UploadSession
{
    public const int RoleMaxLength = 10;
    public const int PurposeMaxLength = 40;
    public const int KindMaxLength = 20;
    public const int ContentTypeMaxLength = 20;
    public const int StagingKeyMaxLength = 200;
    public const int StatusMaxLength = 20;
    public const int FailureCodeMaxLength = 100;

    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    /// <summary>Papel do token no pedido: a confirmação precisa vir do mesmo papel.</summary>
    public string Role { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    public Guid? ResourceId { get; init; }

    public Guid? PhotoId { get; init; }

    public string? Kind { get; init; }

    /// <summary>O que o cliente declarou e foi assinado na URL (o bucket recusa outro). O formato real é conferido pelo conteúdo.</summary>
    public string DeclaredContentType { get; init; } = string.Empty;

    /// <summary>Tamanho declarado e assinado na URL: o bucket recusa corpo de outro tamanho.</summary>
    public long DeclaredSizeBytes { get; init; }

    /// <summary>Chave na área de espera do bucket privado. Gerada pela API, nunca vem do cliente.</summary>
    public string StagingKey { get; init; } = string.Empty;

    public string Status { get; set; } = UploadStatus.Pending;

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Quando a URL de <c>PUT</c> vence.</summary>
    public DateTimeOffset UploadExpiresAt { get; init; }

    /// <summary>Prazo para confirmar.</summary>
    public DateTimeOffset ConfirmExpiresAt { get; init; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>A resposta do caso de uso (JSON), no upload concluído.</summary>
    public string? ResultJson { get; set; }

    /// <summary>Primeiro código de erro, no upload recusado. Só para consulta.</summary>
    public string? FailureCode { get; set; }
}

/// <summary>Estados de <see cref="UploadSession"/>, como ficam no banco.</summary>
public static class UploadStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
}
