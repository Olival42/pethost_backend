using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.UpdateMyOwner;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using PetHost.Shared.Contracts.Audit;
using Xunit;

namespace PetHost.Modules.Owners.Application.UnitTests.Owners;

public sealed class UpdateMyOwnerCommandHandlerTests
{
    private const string Password = "Tutora@123";

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.CreateVersion7();
    private static readonly AccountProfilePatch Previous = new("Camila", "44999990000", "", null);

    private readonly Mock<IOwnerRepository> _ownerRepository = new();
    private readonly Mock<IOwnersUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Owner _owner = Owner.Create(Id, Cpf.Create("52998224725").Value!, Now);
    private readonly UpdateMyOwnerCommandHandler _sut;

    public UpdateMyOwnerCommandHandlerTests()
    {
        _ownerRepository
            .Setup(r => r.GetByUserIdAsync(Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_owner);

        _ownerAccounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Accounts.Summary(Id, "Camila")]);

        _ownerAccounts
            .Setup(a => a.VerifyPasswordAsync(Id, Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _ownerAccounts
            .Setup(a => a.UpdateProfileAsync(Id, It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountProfilePatch>.Success(Previous));

        _sut = new UpdateMyOwnerCommandHandler(
            _ownerRepository.Object, _unitOfWork.Object, _ownerAccounts.Object, _auditTrail.Object, new FakeTimeProvider(Now.AddDays(1)));
    }

    [Fact]
    public async Task HandleAsync_Should_ChangeCpf_When_PasswordIsRightAndNoPaymentWasMade()
    {
        var result = await _sut.HandleAsync(Command(cpf: "111.444.777-35", currentPassword: Password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Cpf.Should().Be("11144477735");
        result.Value.User!.FullName.Should().Be("Camila");
        _owner.Cpf!.Value.Should().Be("11144477735");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _ownerAccounts.Verify(
            a => a.UpdateProfileAsync(It.IsAny<Guid>(), It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "só o CPF veio: a conta não é tocada");
    }

    [Fact]
    public async Task HandleAsync_Should_UpdateOnlyTheAccount_When_CpfIsNotSent()
    {
        var result = await _sut.HandleAsync(Command(fullName: "Camila Lima"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _ownerAccounts.Verify(
            a => a.UpdateProfileAsync(Id, It.Is<AccountProfilePatch>(p => p.FullName == "Camila Lima" && p.Phone == null), It.IsAny<CancellationToken>()),
            Times.Once);
        _ownerAccounts.Verify(a => a.VerifyPasswordAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPasswordRequired_When_CpfChangesWithoutPassword()
    {
        var result = await _sut.HandleAsync(Command(fullName: "Camila Lima", cpf: "111.444.777-35"), CancellationToken.None);

        result.FirstError!.Field.Should().Be("currentPassword");
        result.FirstError.Code.Should().Be(ErrorCodes.Validation);
        _ownerAccounts.Verify(
            a => a.UpdateProfileAsync(It.IsAny<Guid>(), It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "nada é gravado se o pedido vai ser recusado");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPasswordIncorrect_When_PasswordIsWrong()
    {
        var result = await _sut.HandleAsync(Command(cpf: "111.444.777-35", currentPassword: "Errada@123"), CancellationToken.None);

        result.FirstError!.Message.Should().Be("Current password is incorrect.");
        _ownerRepository.Verify(
            r => r.ExistsByCpfAsync(It.IsAny<Cpf>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "sem a senha, não dá para descobrir se um CPF está cadastrado");
    }

    [Fact]
    public async Task HandleAsync_Should_NotAskPassword_When_CpfIsTheSame()
    {
        var result = await _sut.HandleAsync(Command(cpf: "529.982.247-25"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _ownerRepository.Verify(r => r.ExistsByCpfAsync(It.IsAny<Cpf>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnLocked_When_FirstPaymentHappened()
    {
        _owner.LinkStripeCustomer("cus_123", Now);

        var result = await _sut.HandleAsync(Command(cpf: "111.444.777-35", currentPassword: Password), CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_CPF_LOCKED");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnConflict_When_CpfBelongsToAnotherOwner()
    {
        _ownerRepository
            .Setup(r => r.ExistsByCpfAsync(It.IsAny<Cpf>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.HandleAsync(Command(cpf: "111.444.777-35", currentPassword: Password), CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_CPF_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task HandleAsync_Should_RestoreTheAccount_When_SavingTheCpfFails()
    {
        // Compensação: a conta já foi alterada no Auth; o perfil anterior volta.
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("banco fora do ar"));

        var act = () => _sut.HandleAsync(
            Command(fullName: "Camila Lima", cpf: "111.444.777-35", currentPassword: Password),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _ownerAccounts.Verify(a => a.UpdateProfileAsync(Id, Previous, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ProfileDoesNotExist()
    {
        var result = await _sut.HandleAsync(
            new UpdateMyOwnerCommand(Guid.CreateVersion7(), null, null, null, null, "111.444.777-35", Password),
            CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_NOT_FOUND");
    }

    // ----- data de nascimento: mesma trava do CPF -----

    [Fact]
    public async Task HandleAsync_Should_ChangeBirthDate_When_PasswordIsRightAndNoPaymentWasMade()
    {
        GivenAccountBornOn(new DateOnly(1990, 5, 10));

        var result = await _sut.HandleAsync(Command(birthDate: "1991-02-03", currentPassword: Password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _ownerAccounts.Verify(
            a => a.UpdateProfileAsync(Id, It.Is<AccountProfilePatch>(p => p.BirthDate == "1991-02-03"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_AskPassword_When_BirthDateChanges()
    {
        GivenAccountBornOn(new DateOnly(1990, 5, 10));

        var result = await _sut.HandleAsync(Command(birthDate: "1991-02-03"), CancellationToken.None);

        result.FirstError!.Field.Should().Be("currentPassword");
        _ownerAccounts.Verify(
            a => a.UpdateProfileAsync(It.IsAny<Guid>(), It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBirthDateLocked_When_FirstPaymentHappened()
    {
        GivenAccountBornOn(new DateOnly(1990, 5, 10));
        _owner.LinkStripeCustomer("cus_123", Now);

        var result = await _sut.HandleAsync(Command(birthDate: "1991-02-03", currentPassword: Password), CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_BIRTH_DATE_LOCKED");
        _ownerAccounts.Verify(a => a.VerifyPasswordAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_IgnoreLockAndPassword_When_BirthDateIsTheSame()
    {
        // Mesmo depois do pagamento: mandar a data que já está cadastrada não é troca.
        GivenAccountBornOn(new DateOnly(1990, 5, 10));
        _owner.LinkStripeCustomer("cus_123", Now);

        var result = await _sut.HandleAsync(Command(fullName: "Camila Lima", birthDate: "1990-05-10"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private void GivenAccountBornOn(DateOnly birthDate) =>
        _ownerAccounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Accounts.Summary(Id, "Camila") with { BirthDate = birthDate }]);

    private static UpdateMyOwnerCommand Command(
        string? fullName = null,
        string? cpf = null,
        string? currentPassword = null,
        string? birthDate = null) =>
        new(Id, fullName, null, null, birthDate, cpf, currentPassword);
}

public sealed class UpdateMyOwnerCommandValidatorTests
{
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly UpdateMyOwnerCommandValidator _sut;

    public UpdateMyOwnerCommandValidatorTests()
    {
        _ownerAccounts
            .Setup(a => a.ValidateProfileAsync(It.IsAny<Guid>(), It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _sut = new UpdateMyOwnerCommandValidator(_ownerAccounts.Object);
    }

    [Fact]
    public async Task Validate_Should_MergeCpfAndAccountErrors_When_BothAreInvalid()
    {
        _ownerAccounts
            .Setup(a => a.ValidateProfileAsync(It.IsAny<Guid>(), It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure([Error.Validation("phone", "Phone is invalid."), Error.Validation("address.zipCode", "CEP.")]));

        var result = await _sut.ValidateAsync(
            new UpdateMyOwnerCommand(Guid.CreateVersion7(), null, "abc", null, null, "111.111.111-11", null),
            TestContext.Current.CancellationToken);

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Cpf", "phone", "address.zipCode"]);
    }

    [Fact]
    public async Task Validate_Should_SkipAccount_When_OnlyCpfIsSent()
    {
        var result = await _sut.ValidateAsync(
            new UpdateMyOwnerCommand(Guid.CreateVersion7(), null, null, null, null, "529.982.247-25", null),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        _ownerAccounts.Verify(
            a => a.ValidateProfileAsync(It.IsAny<Guid>(), It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Validate_Should_Pass_When_NothingIsSent()
    {
        (await _sut.ValidateAsync(
            new UpdateMyOwnerCommand(Guid.CreateVersion7(), null, null, null, null, null, null),
            TestContext.Current.CancellationToken)).IsValid.Should().BeTrue();
    }
}
