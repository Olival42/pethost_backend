using PetHost.Shared.Kernel.Domain;

namespace PetHost.Modules.Hosts.Domain.IdentityBlocks;

/// <summary>
/// CPF que não abre mais solicitação de anfitrião, em conta nenhuma: houve recusa por suspeita de
/// fraude ou as tentativas acabaram. Espelha a tabela <c>host.identity_blocks</c>.
/// </summary>
/// <remarks>
/// Guarda só a <b>impressão</b> do CPF (<see cref="CpfFingerprint"/>, HMAC-SHA256 com uma chave da
/// configuração), nunca o CPF: o bloqueio sobrevive à exclusão da conta (LGPD, prevenção à fraude,
/// art. 11, II, "g") sem guardar o dado. O bloqueio da conta (pelo histórico das solicitações) continua
/// valendo; este cobre a mesma pessoa com outra conta, ou depois de excluir a dela.
/// </remarks>
public sealed class IdentityBlock : Entity<IdentityBlockId>
{
    /// <summary>Tamanho da impressão: SHA-256 em hexadecimal minúsculo.</summary>
    public const int FingerprintLength = 64;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private IdentityBlock()
    {
        CpfFingerprint = null!;
    }

    private IdentityBlock(string cpfFingerprint, IdentityBlockReason reason, Guid applicationId, DateTimeOffset now)
        : base(IdentityBlockId.New())
    {
        CpfFingerprint = cpfFingerprint;
        Reason = reason;
        ApplicationId = applicationId;
        BlockedAt = now;
    }

    /// <summary>Impressão do CPF. Única: um bloqueio por CPF.</summary>
    public string CpfFingerprint { get; private set; }

    public IdentityBlockReason Reason { get; private set; }

    /// <summary>A solicitação cuja recusa bloqueou (<c>host.host_applications.id</c>). Pode já ter sido apagada.</summary>
    public Guid ApplicationId { get; private set; }

    public DateTimeOffset BlockedAt { get; private set; }

    public static IdentityBlock Create(string cpfFingerprint, IdentityBlockReason reason, Guid applicationId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(cpfFingerprint) || cpfFingerprint.Length != FingerprintLength)
            throw new ArgumentException($"The CPF fingerprint must have {FingerprintLength} characters.", nameof(cpfFingerprint));

        if (applicationId == Guid.Empty)
            throw new ArgumentException("The block must point to an application.", nameof(applicationId));

        return new IdentityBlock(cpfFingerprint, reason, applicationId, now);
    }
}
