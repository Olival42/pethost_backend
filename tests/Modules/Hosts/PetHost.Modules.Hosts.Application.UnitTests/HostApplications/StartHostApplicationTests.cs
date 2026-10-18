using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.IdentityBlocks;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Hosts.Application.HostApplications;
using PetHost.Modules.Hosts.Application.HostApplications.GetMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.StartHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.UpdateMyHostApplication;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Identity;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.HostApplications;

/// <summary>Abrir, corrigir e consultar a própria solicitação.</summary>
public sealed class StartHostApplicationTests
{
    private readonly Mock<IHostApplicationRepository> _applications = new();
    private readonly Mock<IHostRepository> _hosts = new();
    private readonly Mock<IHostsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<ICpfOwnership> _cpfOwnership = new();
    private readonly Mock<IIdentityBlockRepository> _identityBlocks = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly List<HostApplication> _history = [];

    public StartHostApplicationTests()
    {
        _applications.Setup(r => r.ListByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _history);
    }

    private StartHostApplicationCommandHandler Start() =>
        new(_applications.Object, _hosts.Object, _unitOfWork.Object, _cpfOwnership.Object, _identityBlocks.Object, Fingerprint(), _auditTrail.Object, _time);

    private UpdateMyHostApplicationCommandHandler Update() =>
        new(_applications.Object, _hosts.Object, _unitOfWork.Object, _cpfOwnership.Object, _identityBlocks.Object, Fingerprint(), _auditTrail.Object, _time);

    private void Blocked(string cpfText) =>
        _identityBlocks
            .Setup(b => b.FindAsync(FingerprintOf(cpfText), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityBlock.Create(FingerprintOf(cpfText), IdentityBlockReason.Fraud, Guid.CreateVersion7(), Now));

    // ----- CPF bloqueado (fraude ou tentativas esgotadas, em qualquer conta) -----

    [Fact]
    public async Task Start_Should_ReturnIdentityBlocked_When_TheCpfWasBlockedByAnotherAccount()
    {
        // Arrange: a conta é nova (sem histórico), mas o CPF foi recusado por fraude em outra.
        Blocked(CpfText);

        // Act
        var result = await Start().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.IdentityBlocked);
        _applications.Verify(r => r.Add(It.IsAny<HostApplication>()), Times.Never);
        _cpfOwnership.Verify(o => o.IsHeldByAnotherPersonAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnIdentityBlocked_When_TheNewCpfIsBlocked()
    {
        // Arrange
        var draft = Draft();
        _history.Add(draft);
        Blocked(OtherCpfText);

        // Act
        var result = await Update().HandleAsync(new UpdateMyHostApplicationCommand(UserId, OtherCpfText, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.IdentityBlocked);
        draft.Cpf.Value.Should().Be("52998224725");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static StartHostApplicationCommand Command(string? cpf = CpfText) =>
        new(UserId, cpf, "cnh", true, "203.0.113.7");

    // ----- abrir -----

    [Fact]
    public async Task Start_Should_OpenADraftAndRecordTheMaskedCpf_When_AccountCanStart()
    {
        // Act
        var result = await Start().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("draft");
        result.Value.Cpf.Should().Be("52998224725");
        result.Value.DocumentType.Should().Be("cnh");
        result.Value.MissingDocuments.Should().Equal("front", "back", "selfie");
        _applications.Verify(r => r.Add(It.Is<HostApplication>(a => a.NoticeVersion == HostVerificationNotice.CurrentVersion && a.NoticeIpAddress == "203.0.113.7")), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationStarted && r.Details!["cpf"] == "***.***.247-25"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_Should_ReturnAlreadyVerified_When_AccountIsAlreadyAHost()
    {
        // Arrange
        _hosts.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Host.CreateIndividual(UserId, AnyCpf, Guid.CreateVersion7(), AdminId, Now));

        // Act
        var result = await Start().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_ALREADY_VERIFIED");
        _applications.Verify(r => r.Add(It.IsAny<HostApplication>()), Times.Never);
    }

    [Fact]
    public async Task Start_Should_ReturnAlreadyOpen_When_ThereIsAnOpenApplication()
    {
        // Arrange
        _history.Add(Draft());

        // Act
        var result = await Start().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_ALREADY_EXISTS");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_Should_ReturnCpfAlreadyRegistered_When_AnotherPersonUsesTheCpf()
    {
        // Arrange
        _cpfOwnership.Setup(c => c.IsHeldByAnotherPersonAsync("52998224725", UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await Start().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_CPF_ALREADY_REGISTERED");
        _applications.Verify(r => r.Add(It.IsAny<HostApplication>()), Times.Never);
    }

    // ----- pessoa jurídica -----

    private static readonly AddressData CompanyAddress = new("87013-000", "Av. Brasil", "1500", null, "Centro", "Maringá", "PR");

    private static StartHostApplicationCommand CompanyCommand(string cnpj = "11.222.333/0001-81") =>
        new(UserId, CpfText, "cnh", true, null, "pj", cnpj, "Pet Feliz Hospedagem LTDA", "Pet Feliz", CompanyAddress);

    [Fact]
    public async Task Start_Should_OpenACompanyApplicationWithTheRepresentativeCpf_When_CompanyDataIsValid()
    {
        // Act
        var result = await Start().HandleAsync(CompanyCommand(), CancellationToken.None);

        // Assert
        result.Value!.PersonType.Should().Be("pj");
        result.Value.Cpf.Should().Be("52998224725");
        result.Value.Company!.Cnpj.Should().Be("11222333000181");
        result.Value.Company.TradeName.Should().Be("Pet Feliz");
        result.Value.Company.Address.City.Should().Be("Maringá");
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Details!["personType"] == "pj" && r.Details["cnpj"] == "11222333000181"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_Should_ReturnCnpjAlreadyRegistered_When_AHostHasTheCnpj()
    {
        // Arrange
        _hosts.Setup(h => h.ExistsByCnpjAsync(It.IsAny<Cnpj>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await Start().HandleAsync(CompanyCommand(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_CNPJ_ALREADY_REGISTERED");
        _applications.Verify(r => r.Add(It.IsAny<HostApplication>()), Times.Never);
    }

    [Fact]
    public async Task Start_Should_ReturnCnpjAlreadyRegistered_When_AnotherAccountHasAnOpenApplicationWithTheCnpj()
    {
        // Arrange
        _applications.Setup(r => r.ExistsOpenWithCnpjAsync(It.IsAny<Cnpj>(), UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await Start().HandleAsync(CompanyCommand(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_CNPJ_ALREADY_REGISTERED");
    }

    [Fact]
    public void Validator_Should_ReturnEveryCompanyField_When_ACompanySendsNothing()
    {
        // Act
        var result = new StartHostApplicationCommandValidator()
            .Validate(new StartHostApplicationCommand(UserId, CpfText, "cnh", true, null, "pj"));

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().Contain(["cnpj", "legalName", "tradeName", "companyAddress.zipCode", "companyAddress.city"]);
    }

    [Fact]
    public void Validator_Should_RefuseCompanyData_When_ThePersonIsIndividual()
    {
        // Act
        var result = new StartHostApplicationCommandValidator()
            .Validate(new StartHostApplicationCommand(UserId, CpfText, "cnh", true, null, "pf", "11222333000181"));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "cnpj");
    }

    [Fact]
    public void Validator_Should_RefuseAnUnknownPersonType_When_Validating()
    {
        // Act
        var result = new StartHostApplicationCommandValidator()
            .Validate(new StartHostApplicationCommand(UserId, CpfText, "cnh", true, null, "ngo"));

        // Assert
        result.Errors.Should().Contain(e => e.PropertyName == "PersonType");
    }

    [Fact]
    public async Task Update_Should_ReturnCompanyDataNotAllowed_When_TheApplicationIsIndividual()
    {
        // Arrange
        _history.Add(Draft());

        // Act
        var result = await Update().HandleAsync(
            new UpdateMyHostApplicationCommand(UserId, null, null, "11222333000181", "Razão", "Fantasia", CompanyAddress),
            CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_COMPANY_DATA_NOT_ALLOWED");
    }

    [Fact]
    public async Task Update_Should_ReplaceTheCompanyData_When_TheApplicationIsACompany()
    {
        // Arrange
        await Start().HandleAsync(CompanyCommand(), CancellationToken.None);
        _history.Add(CapturedApplication());

        // Act
        var result = await Update().HandleAsync(
            new UpdateMyHostApplicationCommand(UserId, null, null, "11222333000181", "Pet Feliz Hospedagem LTDA", "Cantinho Pet Feliz", CompanyAddress),
            CancellationToken.None);

        // Assert
        result.Value!.Company!.TradeName.Should().Be("Cantinho Pet Feliz");
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationUpdated && r.Details!["fields"] == "company"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_KeepTheRestOfTheCompany_When_OnlyTheCnpjIsSent()
    {
        // Arrange
        await Start().HandleAsync(CompanyCommand(), CancellationToken.None);
        _history.Add(CapturedApplication());

        // Act
        var result = await Update().HandleAsync(
            new UpdateMyHostApplicationCommand(UserId, null, null, Cnpj: "11.444.777/0001-61"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Company!.Cnpj.Should().Be("11444777000161");
        result.Value.Company.LegalName.Should().Be("Pet Feliz Hospedagem LTDA");
        result.Value.Company.Address.City.Should().Be("Maringá");
    }

    [Fact]
    public async Task Update_Should_ChangeOnlyTheSentAddressFields_When_TheAddressIsPartial()
    {
        // Arrange
        await Start().HandleAsync(CompanyCommand(), CancellationToken.None);
        _history.Add(CapturedApplication());

        // Act
        var result = await Update().HandleAsync(
            new UpdateMyHostApplicationCommand(UserId, null, null, CompanyAddress: new AddressData(null, null, "2000", null, null, null, null)),
            CancellationToken.None);

        // Assert
        result.Value!.Company!.Address.Number.Should().Be("2000");
        result.Value.Company.Address.Street.Should().Be("Av. Brasil");
        result.Value.Company.Cnpj.Should().Be("11222333000181");
    }

    [Fact]
    public async Task Update_Should_ReturnOnlyTheCnpjError_When_OnlyAnInvalidCnpjIsSent()
    {
        // Arrange
        await Start().HandleAsync(CompanyCommand(), CancellationToken.None);
        _history.Add(CapturedApplication());

        // Act
        var result = await Update().HandleAsync(
            new UpdateMyHostApplicationCommand(UserId, null, null, Cnpj: "11.222.333/0001-00"),
            CancellationToken.None);

        // Assert
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("cnpj");
    }

    private HostApplication CapturedApplication() =>
        (HostApplication)_applications.Invocations.Single(i => i.Method.Name == nameof(IHostApplicationRepository.Add)).Arguments[0]!;

    [Fact]
    public async Task Start_Should_NotRecordTheTrail_When_SavingFails()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("HOST_APPLICATION_ALREADY_EXISTS", "race")));

        // Act
        var result = await Start().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_ALREADY_EXISTS");
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Validator_Should_ReturnEveryInvalidField_When_TheBodyIsWrong()
    {
        // Act
        var result = new StartHostApplicationCommandValidator().Validate(new StartHostApplicationCommand(UserId, "123", "passport", false, null));

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Cpf", "DocumentType", "AcceptNotice"]);
    }

    [Fact]
    public void Validator_Should_RequireTheNotice_When_ItIsMissing()
    {
        // Act
        var result = new StartHostApplicationCommandValidator().Validate(new StartHostApplicationCommand(UserId, CpfText, "rg", null, null));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "AcceptNotice");
    }

    // ----- corrigir -----

    [Fact]
    public async Task Update_Should_ChangeTheCpfAndRecordTheField_When_Draft()
    {
        // Arrange
        _history.Add(Draft());

        // Act
        var result = await Update().HandleAsync(new UpdateMyHostApplicationCommand(UserId, OtherCpfText, null), CancellationToken.None);

        // Assert
        result.Value!.Cpf.Should().Be("11144477735");
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationUpdated && r.Details!["fields"] == "cpf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_NotSave_When_NothingChanges()
    {
        // Arrange
        _history.Add(Draft());

        // Act
        var result = await Update().HandleAsync(new UpdateMyHostApplicationCommand(UserId, CpfText, "cnh"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnCpfAlreadyRegistered_When_TheNewCpfIsTaken()
    {
        // Arrange
        _history.Add(Draft());
        _cpfOwnership.Setup(c => c.IsHeldByAnotherPersonAsync("11144477735", UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await Update().HandleAsync(new UpdateMyHostApplicationCommand(UserId, OtherCpfText, null), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_CPF_ALREADY_REGISTERED");
        _history[0].Cpf.Should().Be(AnyCpf);
    }

    [Fact]
    public async Task Update_Should_ReturnNotEditable_When_AlreadySubmitted()
    {
        // Arrange
        _history.Add(Submitted());

        // Act
        var result = await Update().HandleAsync(new UpdateMyHostApplicationCommand(UserId, OtherCpfText, null), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_EDITABLE");
        _cpfOwnership.Verify(c => c.IsHeldByAnotherPersonAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_ThereIsNoOpenApplication()
    {
        // Act
        var result = await Update().HandleAsync(new UpdateMyHostApplicationCommand(UserId, null, "rg"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_FOUND");
    }

    // ----- consultar -----

    [Fact]
    public async Task GetMine_Should_LetTheAccountStart_When_ItNeverApplied()
    {
        // Act
        var result = await new GetMyHostApplicationQueryHandler(_applications.Object)
            .HandleAsync(new GetMyHostApplicationQuery(UserId), CancellationToken.None);

        // Assert
        result.Value!.Application.Should().BeNull();
        result.Value.CanStartNew.Should().BeTrue();
        result.Value.RemainingAttempts.Should().Be(HostApplication.MaxAttempts);
        result.Value.NoticeVersion.Should().Be(HostVerificationNotice.CurrentVersion);
    }

    [Fact]
    public async Task GetMine_Should_ShowTheLatestRejection_When_TheAccountWasRejected()
    {
        // Arrange
        _history.Add(Rejected());

        // Act
        var result = await new GetMyHostApplicationQueryHandler(_applications.Object)
            .HandleAsync(new GetMyHostApplicationQuery(UserId), CancellationToken.None);

        // Assert
        result.Value!.Application!.Status.Should().Be("rejected");
        result.Value.Application.RejectionReason.Should().Be("document_illegible");
        result.Value.CanStartNew.Should().BeTrue();
        result.Value.RemainingAttempts.Should().Be(2);
    }
}
