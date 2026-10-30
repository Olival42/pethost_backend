using PetHost.Modules.Hosts.Domain.IdentityBlocks;
using Moq;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.UnitTests.HostApplications;

/// <summary>Dados e dublês comuns aos testes da solicitação de anfitrião.</summary>
internal static class HostsTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid UserId = Guid.CreateVersion7();
    public static readonly Guid AdminId = Guid.CreateVersion7();

    public const string CpfText = "529.982.247-25";
    public const string OtherCpfText = "111.444.777-35";

    public static Cpf AnyCpf => Cpf.Create(CpfText).Value!;

    public static AccountSummary Account(Guid userId) =>
        new(userId, "Dona Cida", "cida@exemplo.com", "host", "44999990000", null, new DateOnly(1968, 3, 15), null, true, Now);

    public static HostApplication Draft(Guid? userId = null) =>
        HostApplication.Start([], userId ?? UserId, AnyCpf, IdentityDocumentType.Cnh, null, "v1", null, Now).Value!;

    public static HostApplication WithAllPhotos(Guid? userId = null)
    {
        var application = Draft(userId);
        application.AttachDocument(DocumentKind.Front, "host-applications/front.jpg", new string('f', 64), 10, Now);
        application.AttachDocument(DocumentKind.Back, "host-applications/back.jpg", new string('b', 64), 10, Now);
        application.AttachDocument(DocumentKind.Selfie, "host-applications/selfie.jpg", new string('s', 64), 10, Now);
        return application;
    }

    public static HostApplication Submitted(Guid? userId = null)
    {
        var application = WithAllPhotos(userId);
        application.Submit(Now);
        return application;
    }

    public static HostApplication Rejected()
    {
        var application = Submitted();
        application.Reject(AdminId, RejectionReason.DocumentIllegible, "x", Now);
        return application;
    }

    /// <summary>Prazo de guarda e de rascunho para o teste.</summary>
    public static IHostVerificationPolicy Policy(TimeSpan retention, int draftDays = 7)
    {
        var policy = new Mock<IHostVerificationPolicy>();
        policy.Setup(p => p.DocumentRetention).Returns(retention);
        policy.Setup(p => p.DraftLifetime).Returns(TimeSpan.FromDays(draftDays));
        return policy.Object;
    }

    public static Mock<IHostsUnitOfWork> UnitOfWork()
    {
        var unitOfWork = new Mock<IHostsUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        return unitOfWork;
    }

    public static Mock<IHostAccounts> Accounts()
    {
        var accounts = new Mock<IHostAccounts>();
        accounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => [.. ids.Select(Account)]);
        return accounts;
    }

    /// <summary>Impressão de mentira, determinística: o CPF completado com zeros até 64 caracteres.</summary>
    public static ICpfFingerprint Fingerprint()
    {
        var fingerprint = new Mock<ICpfFingerprint>();
        fingerprint.Setup(f => f.Of(It.IsAny<Cpf>())).Returns((Cpf cpf) => cpf.Value.PadLeft(IdentityBlock.FingerprintLength, '0'));
        return fingerprint.Object;
    }

    public static string FingerprintOf(string cpfText) => Cpf.Create(cpfText).Value!.Value.PadLeft(IdentityBlock.FingerprintLength, '0');

    public static ImageUpload Image() => new(new MemoryStream([1, 2, 3]), "x.jpg", "image/jpeg");
}
