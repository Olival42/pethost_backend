using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.ChangeMyHostAvatar;
using PetHost.Modules.Hosts.Application.Hosts.DeactivateMyHost;
using PetHost.Modules.Hosts.Application.Hosts.GetHostById;
using PetHost.Modules.Hosts.Application.Hosts.ReactivateHost;
using PetHost.Modules.Hosts.Application.Hosts.SuspendHost;
using PetHost.Modules.Hosts.Application.Hosts.UpdateMyHost;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.Hosts;

/// <summary>Perfil (com a identidade travada), foto, inativar/reativar e as ações do admin.</summary>
public sealed class HostLifecycleTests
{
    private static readonly AccountProfilePatch Previous = new("Dona Cida", "44999990000", "", null);

    private readonly Mock<IHostRepository> _hosts = new();
    private readonly Mock<IHostsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IHostAccounts> _accounts = Accounts();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IImageStorage> _images = new();
    private readonly FakeTimeProvider _time = new(Now.AddDays(1));
    private readonly Host _individual = Host.CreateIndividual(UserId, AnyCpf, Guid.CreateVersion7(), AdminId, Now);
    private readonly Host _company = Host.CreateCompany(
        UserId,
        AnyCpf,
        Cnpj.Create("11222333000181").Value!,
        "Pet Feliz Hospedagem LTDA",
        "Pet Feliz",
        CompanyAddress.Create("87013000", "Av. Brasil", "1500", null, "Centro", "Maringá", "PR").Value!,
        Guid.CreateVersion7(),
        AdminId,
        Now).Value!;

    public HostLifecycleTests()
    {
        _accounts.Setup(a => a.UpdateProfileAsync(UserId, It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountProfilePatch>.Success(Previous));
        _accounts.Setup(a => a.DeactivateAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _accounts.Setup(a => a.SuspendAsync(UserId, It.IsAny<string?>(), AdminId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _accounts.Setup(a => a.LiftSuspensionAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
    }

    private void Is(Host host)
    {
        _hosts.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(host);
        _hosts.Setup(h => h.GetByIdAsync(host.Id, It.IsAny<CancellationToken>())).ReturnsAsync(host);
    }

    private UpdateMyHostCommandHandler Update() => new(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time);

    private static UpdateMyHostCommand Patch(
        string? fullName = null, string? phone = null, string? birthDate = null, string? cpf = null,
        string? cnpj = null, string? legalName = null, string? tradeName = null, AddressData? companyAddress = null) =>
        new(UserId, fullName, phone, null, birthDate, cpf, cnpj, legalName, tradeName, companyAddress);

    // ----- trava de identidade -----

    [Theory]
    [InlineData("Maria Aparecida", null, null)]
    [InlineData(null, "1970-01-01", null)]
    [InlineData(null, null, "111.444.777-35")]
    public async Task Update_Should_ReturnIdentityLocked_When_AVerifiedFieldChanges(string? fullName, string? birthDate, string? cpf)
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await Update().HandleAsync(Patch(fullName: fullName, birthDate: birthDate, cpf: cpf), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_IDENTITY_LOCKED");
        _accounts.Verify(a => a.UpdateProfileAsync(It.IsAny<Guid>(), It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("12.345.678/0001-95", null)]
    [InlineData(null, "Outra Razão LTDA")]
    public async Task Update_Should_ReturnIdentityLocked_When_TheCompanyIdentityChanges(string? cnpj, string? legalName)
    {
        // Arrange
        Is(_company);

        // Act
        var result = await Update().HandleAsync(Patch(cnpj: cnpj, legalName: legalName), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_IDENTITY_LOCKED");
    }

    [Fact]
    public async Task Update_Should_AcceptTheVerifiedValues_When_TheyAreUnchanged()
    {
        // Arrange: o front manda o formulário inteiro, com nome, nascimento e CPF iguais.
        Is(_individual);

        // Act
        var result = await Update().HandleAsync(
            Patch(fullName: "Dona Cida", birthDate: "1968-03-15", cpf: CpfText, phone: "(44) 3222-1111"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _accounts.Verify(a => a.UpdateProfileAsync(UserId, It.Is<AccountProfilePatch>(p => p.FullName == null && p.Phone == "(44) 3222-1111"), It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ChangeTheCompanyContactAndRecordIt_When_Company()
    {
        // Arrange
        Is(_company);

        // Act
        var result = await Update().HandleAsync(
            Patch(tradeName: "Cantinho Pet Feliz", companyAddress: new AddressData(null, "Rua Nova", "10", null, null, null, null)),
            CancellationToken.None);

        // Assert
        result.Value!.Company!.TradeName.Should().Be("Cantinho Pet Feliz");
        result.Value.Company.Address.Street.Should().Be("Rua Nova");
        result.Value.Company.Address.City.Should().Be("Maringá", "o endereço parcial é completado com o atual");
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostUpdated && r.Details!["fields"] == "tradeName,companyAddress"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_ReturnNotACompany_When_AnIndividualSendsCompanyData()
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await Update().HandleAsync(Patch(tradeName: "Pet Feliz"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_NOT_A_COMPANY");
    }

    [Fact]
    public async Task Update_Should_RestoreTheAccount_When_SavingTheCompanyFails()
    {
        // Arrange
        Is(_company);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(new Error("HOST_APPLICATION_CONFLICT", "race")));

        // Act
        var result = await Update().HandleAsync(Patch(phone: "44988887777", tradeName: "Novo nome"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_CONFLICT");
        _accounts.Verify(a => a.UpdateProfileAsync(UserId, Previous, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_NotApprovedYet()
    {
        // Act
        var result = await Update().HandleAsync(Patch(phone: "44988887777"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_NOT_FOUND");
    }

    // ----- foto -----

    [Fact]
    public async Task ChangeAvatar_Should_StoreTheImageAndDeleteThePrevious_When_HostExists()
    {
        // Arrange
        Is(_individual);
        _accounts.Setup(a => a.UpdateProfileAsync(UserId, It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountProfilePatch>.Success(Previous with { AvatarUrl = "http://img/avatars/old.png" }));
        _images.Setup(i => i.SaveAsync(It.IsAny<ImageUpload?>(), ImageFolders.Avatars, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StoredImage>.Success(new StoredImage("http://img/avatars/new.png", new string('a', 64))));

        // Act
        var result = await new ChangeMyHostAvatarCommandHandler(_hosts.Object, _accounts.Object, _images.Object)
            .HandleAsync(new ChangeMyHostAvatarCommand(UserId, Image()), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _accounts.Verify(a => a.UpdateProfileAsync(UserId, It.Is<AccountProfilePatch>(p => p.AvatarUrl == "http://img/avatars/new.png"), It.IsAny<CancellationToken>()), Times.Once);
        _images.Verify(i => i.DeleteAsync("http://img/avatars/old.png", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeAvatar_Should_NotTouchTheBucket_When_NotApprovedYet()
    {
        // Act
        var result = await new ChangeMyHostAvatarCommandHandler(_hosts.Object, _accounts.Object, _images.Object)
            .HandleAsync(new ChangeMyHostAvatarCommand(UserId, Image()), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_NOT_FOUND");
        _images.Verify(i => i.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAvatar_Should_ClearThePhoto_When_HostExists()
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await new RemoveMyHostAvatarCommandHandler(_hosts.Object, _accounts.Object, _images.Object)
            .HandleAsync(new RemoveMyHostAvatarCommand(UserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _accounts.Verify(a => a.UpdateProfileAsync(UserId, It.Is<AccountProfilePatch>(p => p.AvatarUrl == ""), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ----- inativar / reativar -----

    [Fact]
    public async Task Deactivate_Should_DeactivateProfileAndAccount_When_Approved()
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await Deactivate().HandleAsync(new DeactivateMyHostCommand(UserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _individual.IsActive.Should().BeFalse();
        _accounts.Verify(a => a.DeactivateAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostDeactivated), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deactivate_Should_DeactivateOnlyTheAccount_When_NotApprovedYet()
    {
        // Act
        var result = await Deactivate().HandleAsync(new DeactivateMyHostCommand(UserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _accounts.Verify(a => a.DeactivateAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deactivate_Should_ReactivateTheProfileAgain_When_TheAccountCannotBeDeactivated()
    {
        // Arrange
        Is(_individual);
        _accounts.Setup(a => a.DeactivateAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(new Error("AUTH_USER_NOT_FOUND", "x")));

        // Act
        var result = await Deactivate().HandleAsync(new DeactivateMyHostCommand(UserId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("AUTH_USER_NOT_FOUND");
        _individual.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Reactivate_Should_ReactivateTheProfileAndOpenASession_When_CredentialsAreRight()
    {
        // Arrange
        Is(_individual);
        _individual.Deactivate(Now);
        _accounts.Setup(a => a.ReactivateAsync("cida@exemplo.com", "Senha@123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountSession>.Success(new AccountSession("jwt", "refresh", 1, Account(UserId))));

        // Act
        var result = await new ReactivateHostCommandHandler(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time)
            .HandleAsync(new ReactivateHostCommand("cida@exemplo.com", "Senha@123"), CancellationToken.None);

        // Assert
        result.Value!.AccessToken.Should().Be("jwt");
        result.Value.Host!.IsActive.Should().BeTrue();
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostReactivated), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reactivate_Should_ReturnTheAccountError_When_Suspended()
    {
        // Arrange
        _accounts.Setup(a => a.ReactivateAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountSession>.Failure(new Error("AUTH_ACCOUNT_SUSPENDED", "x")));

        // Act
        var result = await new ReactivateHostCommandHandler(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time)
            .HandleAsync(new ReactivateHostCommand("cida@exemplo.com", "Senha@123"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("AUTH_ACCOUNT_SUSPENDED");
    }

    // ----- admin -----

    [Fact]
    public async Task Suspend_Should_SuspendProfileAndAccountWithTheReason_When_Called()
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await new SuspendHostCommandHandler(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time)
            .HandleAsync(new SuspendHostCommand(AdminId, _individual.Id.Value, "Documento de terceiro"), CancellationToken.None);

        // Assert
        result.Value!.SuspendedAt.Should().NotBeNull();
        _accounts.Verify(a => a.SuspendAsync(UserId, "Documento de terceiro", AdminId, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostSuspended && r.Reason == "Documento de terceiro"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Suspend_Should_LiftTheProfileAgain_When_TheAccountCannotBeSuspended()
    {
        // Arrange
        Is(_individual);
        _accounts.Setup(a => a.SuspendAsync(UserId, It.IsAny<string?>(), AdminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("AUTH_ADMIN_SUSPENSION_FORBIDDEN", "x")));

        // Act
        var result = await new SuspendHostCommandHandler(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time)
            .HandleAsync(new SuspendHostCommand(AdminId, _individual.Id.Value, "x"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("AUTH_ADMIN_SUSPENSION_FORBIDDEN");
        _individual.IsSuspended.Should().BeFalse();
    }

    [Fact]
    public async Task Suspend_Should_ReturnNotFound_When_HostDoesNotExist()
    {
        // Act
        var result = await new SuspendHostCommandHandler(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time)
            .HandleAsync(new SuspendHostCommand(AdminId, Guid.CreateVersion7(), "x"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_NOT_FOUND");
    }

    [Fact]
    public async Task Lift_Should_LiftProfileAndAccount_When_Suspended()
    {
        // Arrange
        Is(_individual);
        _individual.Suspend(Now);

        // Act
        var result = await new LiftHostSuspensionCommandHandler(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time)
            .HandleAsync(new LiftHostSuspensionCommand(AdminId, _individual.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.SuspendedAt.Should().BeNull();
        _accounts.Verify(a => a.LiftSuspensionAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lift_Should_Return422_When_TheIdentityWasReleased()
    {
        // Arrange
        Is(_individual);
        _individual.Suspend(Now);
        _individual.ReleaseIdentity(Now);

        // Act
        var result = await new LiftHostSuspensionCommandHandler(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time)
            .HandleAsync(new LiftHostSuspensionCommand(AdminId, _individual.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_IDENTITY_RELEASED");
        _accounts.Verify(a => a.LiftSuspensionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Release_Should_RecordTheMaskedCpfAndTheCnpj_When_ACompanyIsSuspended()
    {
        // Arrange
        Is(_company);
        _company.Suspend(Now);

        // Act
        var result = await new ReleaseHostIdentityCommandHandler(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time)
            .HandleAsync(new ReleaseHostIdentityCommand(AdminId, _company.Id.Value, "CNPJ da empresa verdadeira"), CancellationToken.None);

        // Assert
        result.Value!.IdentityReleasedAt.Should().NotBeNull();
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostIdentityReleased && r.Details!["cpf"] == "***.***.247-25" && r.Details["cnpj"] == "11222333000181"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Release_Should_RequireASuspension_When_HostIsActive()
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await new ReleaseHostIdentityCommandHandler(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time)
            .HandleAsync(new ReleaseHostIdentityCommand(AdminId, _individual.Id.Value, "x"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_SUSPENSION_REQUIRED");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ReasonValidators_Should_RequireAReason_When_AdminActs()
    {
        // Act / Assert
        new SuspendHostCommandValidator().Validate(new SuspendHostCommand(AdminId, Guid.CreateVersion7(), "")).IsValid.Should().BeFalse();
        new ReleaseHostIdentityCommandValidator().Validate(new ReleaseHostIdentityCommand(AdminId, Guid.CreateVersion7(), null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task GetById_Should_RecordTheAdminRead_When_Found()
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await new GetHostByIdQueryHandler(_hosts.Object, _accounts.Object, _auditTrail.Object)
            .HandleAsync(new GetHostByIdQuery(_individual.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.User!.FullName.Should().Be("Dona Cida");
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostViewed), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_Should_JoinEveryHostWithItsAccountAndRecordTheListing_When_Called()
    {
        // Arrange
        _hosts.Setup(h => h.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_individual]);

        // Act
        var result = await new ListHostsQueryHandler(_hosts.Object, _accounts.Object, _auditTrail.Object)
            .HandleAsync(new ListHostsQuery(), CancellationToken.None);

        // Assert
        result.Value.Should().ContainSingle().Which.User!.Email.Should().Be("cida@exemplo.com");
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostsListed), It.IsAny<CancellationToken>()), Times.Once);
    }

    private DeactivateMyHostCommandHandler Deactivate() => new(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time);
}
