using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>
/// Solicitação de cadastro de anfitrião: o caso de verificação de identidade que o admin
/// aprova ou recusa. Espelha <c>host.host_applications</c>.
/// </summary>
/// <remarks>
/// A conta de login (Auth) já existe; o perfil de anfitrião (<see cref="Host"/>) só nasce
/// quando o admin aprova. Até lá, os dados e as fotos ficam aqui.
/// <code>
/// draft ──submit──▶ submitted ──approve──▶ approved (cria o Host)
///   │                   ├──reject──▶ rejected (pode abrir outra, até MaxAttempts)
///   │                   └──reject (suspected_fraud)──▶ rejected_final (sem nova tentativa)
///   ├──cancel──▶ cancelled           (submitted também pode ser cancelada)
///   └──expire──▶ expired             (rascunho abandonado)
/// </code>
/// As fotos (<see cref="Documents"/>) só mudam no rascunho e são apagadas depois que a
/// solicitação fecha (<see cref="PurgeDocuments"/>), conforme o prazo de guarda.
/// <para>
/// Pessoa física ou jurídica, escolhida ao abrir e fixa depois. Na jurídica, o CPF e as
/// fotos são do <b>representante legal</b> — a dona da conta — e a solicitação leva também os
/// dados da empresa (<see cref="Company"/>), que vão para o perfil na aprovação.
/// </para>
/// </remarks>
public sealed class HostApplication : Entity<HostApplicationId>
{
    /// <summary>Recusas comuns aceitas antes de a pessoa precisar falar com o suporte.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Limite do motivo/nota do admin, igual ao das outras ações do admin.</summary>
    public const int ReviewNotesMaxLength = 500;

    public const int NoticeVersionMaxLength = 20;

    /// <summary>IPv6 com zona cabe em 45 caracteres.</summary>
    public const int IpAddressMaxLength = 45;

    private readonly List<HostApplicationDocument> _documents = [];

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private HostApplication()
    {
        Cpf = null!;
        NoticeVersion = null!;
    }

    private HostApplication(
        Guid userId,
        Cpf cpf,
        IdentityDocumentType documentType,
        CompanyDetails? company,
        string noticeVersion,
        string? noticeIpAddress,
        int attemptNumber,
        DateTimeOffset now)
        : base(HostApplicationId.New())
    {
        UserId = userId;
        Status = HostApplicationStatus.Draft;
        PersonType = company is null ? PersonType.Individual : PersonType.Company;
        Cpf = cpf;
        SetCompany(company);
        DocumentType = documentType;
        NoticeVersion = noticeVersion;
        NoticeAcceptedAt = now;
        NoticeIpAddress = noticeIpAddress;
        AttemptNumber = attemptNumber;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>A conta (Auth) de quem pede. FK lógica.</summary>
    public Guid UserId { get; private set; }

    public HostApplicationStatus Status { get; private set; }

    /// <summary>Pessoa física ou jurídica. Escolhida ao abrir; não muda.</summary>
    public PersonType PersonType { get; private set; }

    /// <summary>Pessoa física: CPF do anfitrião. Jurídica: CPF do representante legal (dono da conta).</summary>
    public Cpf Cpf { get; private set; }

    /// <summary>Só pessoa jurídica.</summary>
    public Cnpj? Cnpj { get; private set; }

    /// <summary>Razão social. Só pessoa jurídica.</summary>
    public string? LegalName { get; private set; }

    /// <summary>Nome fantasia. Só pessoa jurídica.</summary>
    public string? TradeName { get; private set; }

    /// <summary>Endereço da empresa. Só pessoa jurídica.</summary>
    public CompanyAddress? CompanyAddress { get; private set; }

    /// <summary>Os dados da empresa juntos, ou <c>null</c> na pessoa física.</summary>
    public CompanyDetails? Company =>
        PersonType == PersonType.Company && Cnpj is not null && LegalName is not null && TradeName is not null && CompanyAddress is not null
            ? CompanyDetails.Restore(Cnpj, LegalName, TradeName, CompanyAddress)
            : null;

    public IdentityDocumentType DocumentType { get; private set; }

    /// <summary>Versão do aviso de uso das fotos que a pessoa leu e aceitou (transparência, LGPD).</summary>
    public string NoticeVersion { get; private set; }

    public DateTimeOffset NoticeAcceptedAt { get; private set; }

    /// <summary>IP de quem aceitou o aviso. Dado pessoal: some junto com a linha.</summary>
    public string? NoticeIpAddress { get; private set; }

    /// <summary>1 na primeira solicitação; +1 a cada recusa comum anterior.</summary>
    public int AttemptNumber { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>Quando o admin decidiu.</summary>
    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>O admin que decidiu (users.id). Sem FK, como <c>suspended_by</c>.</summary>
    public Guid? ReviewedBy { get; private set; }

    public RejectionReason? RejectionReason { get; private set; }

    /// <summary>Motivo escrito pelo admin. Interno: a pessoa vê só <see cref="RejectionReason"/>.</summary>
    public string? ReviewNotes { get; private set; }

    /// <summary>Quando a solicitação fechou (decidida, cancelada ou expirada): base do prazo de guarda.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Quando as fotos foram apagadas do bucket. A linha fica, sem imagem, para a trilha.</summary>
    public DateTimeOffset? DocumentsPurgedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<HostApplicationDocument> Documents => _documents.AsReadOnly();

    /// <summary>Ainda não fechou: rascunho ou na fila do admin.</summary>
    public bool IsOpen => Status is HostApplicationStatus.Draft or HostApplicationStatus.Submitted;

    /// <summary>As fotos que ainda faltam para poder enviar.</summary>
    public IReadOnlyList<DocumentKind> MissingDocuments =>
        [.. DocumentKindValues.Required.Where(kind => _documents.All(d => d.Kind != kind))];

    /// <summary>
    /// Abre uma solicitação nova. <paramref name="history"/> são as solicitações anteriores
    /// <b>da mesma conta</b>: é por elas que se sabe se pode abrir outra e qual a tentativa.
    /// </summary>
    /// <param name="company">Dados da empresa: com eles, a solicitação é de pessoa jurídica; sem, física.</param>
    public static Result<HostApplication> Start(
        IReadOnlyCollection<HostApplication> history,
        Guid userId,
        Cpf cpf,
        IdentityDocumentType documentType,
        CompanyDetails? company,
        string noticeVersion,
        string? noticeIpAddress,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(cpf);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeVersion);

        if (userId == Guid.Empty)
            throw new ArgumentException("The application must point to an account.", nameof(userId));

        if (StartBlocker(history) is { } blocker)
            return Result<HostApplication>.Failure(blocker);

        var attempt = RejectedCount(history) + 1;
        var ip = string.IsNullOrWhiteSpace(noticeIpAddress) ? null : noticeIpAddress.Trim();
        if (ip is { Length: > IpAddressMaxLength })
            ip = ip[..IpAddressMaxLength];

        return Result<HostApplication>.Success(
            new HostApplication(userId, cpf, documentType, company, noticeVersion, ip, attempt, now));
    }

    /// <summary>A conta pode abrir uma solicitação nova agora?</summary>
    public static bool CanStart(IReadOnlyCollection<HostApplication> history) => StartBlocker(history) is null;

    /// <summary>Quantas solicitações a conta ainda pode abrir (0 se bloqueada ou já aprovada).</summary>
    public static int RemainingAttempts(IReadOnlyCollection<HostApplication> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (history.Any(a => a.Status is HostApplicationStatus.Approved or HostApplicationStatus.RejectedFinal))
            return 0;

        return Math.Max(0, MaxAttempts - RejectedCount(history));
    }

    /// <summary>
    /// Corrige CPF, tipo de documento e/ou os dados da empresa (inteiros). Só no rascunho;
    /// <c>null</c> = não mexer. Dados de empresa numa solicitação de pessoa física são recusados.
    /// </summary>
    public Result ChangeDetails(Cpf? cpf, IdentityDocumentType? documentType, CompanyDetails? company, DateTimeOffset now)
    {
        if (Status != HostApplicationStatus.Draft)
            return Result.Failure(HostsErrors.ApplicationNotEditable);

        if (company is not null && PersonType != PersonType.Company)
            return Result.Failure(HostsErrors.CompanyDataNotAllowed);

        var changed = false;

        if (company is not null && company != Company)
        {
            SetCompany(company);
            changed = true;
        }

        if (cpf is not null && cpf != Cpf)
        {
            Cpf = cpf;
            changed = true;
        }

        if (documentType is { } type && type != DocumentType)
        {
            DocumentType = type;
            changed = true;
        }

        if (changed)
            UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>A solicitação aceita foto agora? Conferido antes de enviar o arquivo ao bucket.</summary>
    public Result EnsureAcceptsDocuments() =>
        Status == HostApplicationStatus.Draft ? Result.Success() : Result.Failure(HostsErrors.ApplicationNotEditable);

    /// <summary>
    /// Põe a foto na vaga <paramref name="kind"/>, trocando a que estava. Devolve a chave
    /// anterior (para apagar do bucket), ou <c>null</c> se a vaga estava vazia. Recusa a mesma
    /// imagem já usada em outra vaga.
    /// </summary>
    public Result<string?> AttachDocument(DocumentKind kind, string storageKey, string contentHash, long sizeBytes, DateTimeOffset now)
    {
        var attached = AttachDocuments([new NewHostApplicationDocument(kind, storageKey, contentHash, sizeBytes)], now);

        return attached.IsSuccess
            ? Result<string?>.Success(attached.Value!.Count > 0 ? attached.Value[0] : null)
            : Result<string?>.FromFailure(attached);
    }

    /// <summary>
    /// Põe várias fotos de uma vez (cada uma na sua vaga), trocando as que estavam. Tudo ou
    /// nada: a mesma vaga duas vezes, ou a mesma imagem em duas vagas (no envio ou contra as
    /// vagas que ficam), recusa o lote inteiro. Devolve as chaves trocadas, para apagar do
    /// bucket.
    /// </summary>
    public Result<IReadOnlyList<string>> AttachDocuments(IReadOnlyList<NewHostApplicationDocument> documents, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
            throw new ArgumentException("At least one document is required.", nameof(documents));

        foreach (var document in documents)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(document.StorageKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(document.ContentHash);
        }

        if (documents.Select(d => d.Kind).Distinct().Count() != documents.Count)
            throw new ArgumentException("Each document kind can be sent only once.", nameof(documents));

        var accepts = EnsureAcceptsDocuments();
        if (accepts.IsFailure)
            return Result<IReadOnlyList<string>>.FromFailure(accepts);

        // A conferência é contra como as vagas vão ficar: trocar frente e verso entre si passa.
        var kept = _documents.Where(d => documents.All(n => n.Kind != d.Kind)).Select(d => d.ContentHash);
        var hashes = documents.Select(d => d.ContentHash).Concat(kept).ToList();
        if (hashes.Distinct(StringComparer.Ordinal).Count() != hashes.Count)
            return Result<IReadOnlyList<string>>.Failure(HostsErrors.DocumentAlreadyUsed);

        UpdatedAt = now;

        var replaced = new List<string>();
        foreach (var document in documents)
        {
            var existing = _documents.FirstOrDefault(d => d.Kind == document.Kind);
            if (existing is not null)
                replaced.Add(existing.Replace(document.StorageKey, document.ContentHash, document.SizeBytes, now));
            else
                _documents.Add(new HostApplicationDocument(document.Kind, document.StorageKey, document.ContentHash, document.SizeBytes, now));
        }

        return Result<IReadOnlyList<string>>.Success(replaced);
    }

    /// <summary>Envia para a fila do admin. Precisa das três fotos.</summary>
    public Result Submit(DateTimeOffset now)
    {
        if (Status != HostApplicationStatus.Draft)
            return Result.Failure(HostsErrors.ApplicationNotEditable);

        var missing = MissingDocuments;
        if (missing.Count > 0)
            return Result.Failure(HostsErrors.DocumentsMissing(missing));

        Status = HostApplicationStatus.Submitted;
        SubmittedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Aprova e cria o perfil de anfitrião com os dados conferidos. Só de solicitação na fila.
    /// </summary>
    /// <param name="adminId">O admin que conferiu (vem do token).</param>
    /// <param name="reason">O que o admin conferiu. Vai para a nota e para a trilha.</param>
    public Result<Host> Approve(Guid adminId, string? reason, DateTimeOffset now)
    {
        if (Status != HostApplicationStatus.Submitted)
            return Result<Host>.Failure(HostsErrors.ApplicationNotUnderReview);

        Result<Host> host;
        if (Company is { } company)
        {
            host = Host.CreateCompany(UserId, Cpf, company.Cnpj, company.LegalName, company.TradeName, company.Address, Id.Value, adminId, now);
            if (host.IsFailure)
                return host;
        }
        else
        {
            host = Result<Host>.Success(Host.CreateIndividual(UserId, Cpf, Id.Value, adminId, now));
        }

        Close(HostApplicationStatus.Approved, adminId, reason, now);

        return host;
    }

    /// <summary>
    /// Recusa com motivo. <see cref="Domain.HostApplications.RejectionReason.SuspectedFraud"/>
    /// fecha de vez (<see cref="HostApplicationStatus.RejectedFinal"/>); os outros deixam abrir
    /// uma nova solicitação.
    /// </summary>
    public Result Reject(Guid adminId, RejectionReason reason, string? notes, DateTimeOffset now)
    {
        if (Status != HostApplicationStatus.Submitted)
            return Result.Failure(HostsErrors.ApplicationNotUnderReview);

        var status = reason == Domain.HostApplications.RejectionReason.SuspectedFraud
            ? HostApplicationStatus.RejectedFinal
            : HostApplicationStatus.Rejected;

        Close(status, adminId, notes, now);
        RejectionReason = reason;

        return Result.Success();
    }

    /// <summary>A pessoa desiste: vale no rascunho e na fila, antes da decisão.</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (!IsOpen)
            return Result.Failure(HostsErrors.ApplicationClosed);

        Status = HostApplicationStatus.Cancelled;
        ClosedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Fecha um rascunho abandonado. Devolve <c>false</c> (e não muda nada) se não é rascunho.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Status != HostApplicationStatus.Draft)
            return false;

        Status = HostApplicationStatus.Expired;
        ClosedAt = now;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// Tira as fotos da solicitação fechada e devolve as chaves, para apagar do bucket.
    /// Solicitação aberta não perde as fotos (devolve vazio).
    /// </summary>
    public IReadOnlyList<string> PurgeDocuments(DateTimeOffset now)
    {
        if (IsOpen || _documents.Count == 0)
            return [];

        var keys = _documents.Select(d => d.StorageKey).ToList();

        _documents.Clear();
        DocumentsPurgedAt = now;
        UpdatedAt = now;

        return keys;
    }

    private void SetCompany(CompanyDetails? company)
    {
        Cnpj = company?.Cnpj;
        LegalName = company?.LegalName;
        TradeName = company?.TradeName;
        CompanyAddress = company?.Address;
    }

    private void Close(HostApplicationStatus status, Guid adminId, string? notes, DateTimeOffset now)
    {
        Status = status;
        ReviewedAt = now;
        ReviewedBy = adminId;
        ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ClosedAt = now;
        UpdatedAt = now;
    }

    private static int RejectedCount(IReadOnlyCollection<HostApplication> history) =>
        history.Count(a => a.Status == HostApplicationStatus.Rejected);

    private static Error? StartBlocker(IReadOnlyCollection<HostApplication> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (history.Any(a => a.Status == HostApplicationStatus.Approved))
            return HostsErrors.AlreadyVerified;

        if (history.Any(a => a.IsOpen))
            return HostsErrors.ApplicationAlreadyOpen;

        if (history.Any(a => a.Status == HostApplicationStatus.RejectedFinal))
            return HostsErrors.ApplicationBlocked;

        if (RejectedCount(history) >= MaxAttempts)
            return HostsErrors.AttemptsExceeded;

        return null;
    }
}
