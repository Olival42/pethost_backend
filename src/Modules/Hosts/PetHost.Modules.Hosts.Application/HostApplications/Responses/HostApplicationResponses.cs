using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Hosts.Application.HostApplications.Responses;

/// <summary>
/// A solicitação como a própria pessoa vê: status, dados enviados, quais fotos já foram e o
/// motivo da recusa. Sem as fotos (nem link) e sem a nota interna do admin.
/// </summary>
/// <param name="Cpf">Só dígitos, sem máscara: é o CPF que a própria pessoa digitou (na empresa, o do representante).</param>
/// <param name="Company">Dados da empresa. Só pessoa jurídica.</param>
/// <param name="MissingDocuments">Fotos que faltam para enviar (<c>front</c>, <c>back</c>, <c>selfie</c>).</param>
/// <param name="RejectionReason">Motivo da recusa (<c>document_illegible</c>...). O front traduz.</param>
public sealed record HostApplicationResponse(
    Guid Id,
    string Status,
    string PersonType,
    string Cpf,
    string DocumentType,
    CompanyResponse? Company,
    int AttemptNumber,
    IReadOnlyList<HostApplicationDocumentResponse> Documents,
    IReadOnlyList<string> MissingDocuments,
    string? RejectionReason,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DocumentsPurgedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static HostApplicationResponse From(HostApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new HostApplicationResponse(
            application.Id.Value,
            HostApplicationStatusValues.ToWire(application.Status),
            PersonTypeValues.ToWire(application.PersonType),
            application.Cpf.Value,
            IdentityDocumentTypeValues.ToWire(application.DocumentType),
            CompanyResponse.From(application.Company),
            application.AttemptNumber,
            [.. application.Documents.OrderBy(d => d.Kind).Select(d => new HostApplicationDocumentResponse(DocumentKindValues.ToWire(d.Kind), d.UploadedAt))],
            [.. application.MissingDocuments.Select(DocumentKindValues.ToWire)],
            application.RejectionReason is { } reason ? RejectionReasonValues.ToWire(reason) : null,
            application.SubmittedAt,
            application.ReviewedAt,
            application.ClosedAt,
            application.DocumentsPurgedAt,
            application.CreatedAt,
            application.UpdatedAt);
    }
}

/// <summary>Uma foto já enviada: só qual é e quando. A imagem não volta para quem enviou.</summary>
public sealed record HostApplicationDocumentResponse(string Kind, DateTimeOffset UploadedAt);

/// <summary>
/// A tela "Minha verificação": a solicitação mais recente (ou nenhuma) e se dá para abrir outra.
/// </summary>
/// <param name="Application">A solicitação mais recente da conta. Nula se nunca abriu uma.</param>
/// <param name="CanStartNew">Pode abrir uma solicitação nova agora.</param>
/// <param name="RemainingAttempts">Quantas solicitações ainda pode abrir.</param>
/// <param name="NoticeVersion">Versão vigente do aviso de uso das fotos, para o front mostrar o texto certo.</param>
public sealed record MyHostApplicationResponse(
    HostApplicationResponse? Application,
    bool CanStartNew,
    int RemainingAttempts,
    string NoticeVersion);

/// <summary>
/// A solicitação como o admin vê para decidir: tudo o que a pessoa enviou, os dados da conta
/// para comparar com o documento, a nota do admin e o caminho de cada foto (rota auditada que
/// devolve o link assinado).
/// </summary>
/// <param name="User">Dados da conta (nome, nascimento, endereço) para comparar com o documento.</param>
public sealed record HostApplicationReviewResponse(
    Guid Id,
    Guid UserId,
    string Status,
    string PersonType,
    string Cpf,
    string DocumentType,
    CompanyResponse? Company,
    int AttemptNumber,
    string NoticeVersion,
    DateTimeOffset NoticeAcceptedAt,
    IReadOnlyList<HostApplicationReviewDocumentResponse> Documents,
    IReadOnlyList<string> MissingDocuments,
    string? RejectionReason,
    string? ReviewNotes,
    Guid? ReviewedBy,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DocumentsPurgedAt,
    DateTimeOffset CreatedAt,
    AccountSummary? User)
{
    /// <summary>Onde o admin pede o link da foto: <c>GET</c> com o token de admin (rota auditada).</summary>
    public static string DocumentPath(Guid applicationId, DocumentKind kind) =>
        $"/api/v1/host-applications/{applicationId}/documents/{DocumentKindValues.ToWire(kind)}";

    public static HostApplicationReviewResponse From(HostApplication application, AccountSummary? user)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new HostApplicationReviewResponse(
            application.Id.Value,
            application.UserId,
            HostApplicationStatusValues.ToWire(application.Status),
            PersonTypeValues.ToWire(application.PersonType),
            application.Cpf.Value,
            IdentityDocumentTypeValues.ToWire(application.DocumentType),
            CompanyResponse.From(application.Company),
            application.AttemptNumber,
            application.NoticeVersion,
            application.NoticeAcceptedAt,
            [
                .. application.Documents.OrderBy(d => d.Kind).Select(d => new HostApplicationReviewDocumentResponse(
                    DocumentKindValues.ToWire(d.Kind),
                    d.UploadedAt,
                    d.SizeBytes,
                    DocumentPath(application.Id.Value, d.Kind))),
            ],
            [.. application.MissingDocuments.Select(DocumentKindValues.ToWire)],
            application.RejectionReason is { } reason ? RejectionReasonValues.ToWire(reason) : null,
            application.ReviewNotes,
            application.ReviewedBy,
            application.SubmittedAt,
            application.ReviewedAt,
            application.ClosedAt,
            application.DocumentsPurgedAt,
            application.CreatedAt,
            user);
    }
}

/// <summary>Uma foto para o admin: qual é, quando chegou, tamanho e a rota para pedir o link.</summary>
/// <param name="Path">Rota da API que devolve a URL assinada da imagem. Só admin; cada link vai para a trilha.</param>
public sealed record HostApplicationReviewDocumentResponse(string Kind, DateTimeOffset UploadedAt, long SizeBytes, string Path);

/// <summary>Link temporário para o admin abrir uma foto da solicitação.</summary>
/// <param name="Kind"><c>front</c>, <c>back</c> ou <c>selfie</c>.</param>
/// <param name="Url">
/// URL assinada direto no bucket privado: vai no <c>src</c> da imagem, sem token. Depois de
/// <paramref name="ExpiresAt"/> o bucket responde 403 e é preciso pedir outro link.
/// </param>
/// <param name="ExpiresAt">
/// Quando a URL vence, em Unix time (segundos, UTC) — como o <c>expiresAt</c> da sessão. Use
/// para pedir outro link antes de vencer.
/// </param>
public sealed record HostApplicationDocumentLinkResponse(string Kind, string Url, long ExpiresAt);

/// <summary>O que a tarefa de limpeza fez numa rodada.</summary>
/// <param name="ExpiredDrafts">Rascunhos abandonados que foram fechados.</param>
/// <param name="PurgedApplications">Solicitações que tiveram as fotos apagadas.</param>
public sealed record PurgeHostApplicationsResponse(int ExpiredDrafts, int PurgedApplications);
