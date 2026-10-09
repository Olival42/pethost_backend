using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.GetOwnerById;
using PetHost.Modules.Owners.Application.Owners.ListOwners;
using PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using PetHost.Shared.Contracts.Audit;
using Xunit;

namespace PetHost.Modules.Owners.Application.UnitTests.Owners;

internal static class Accounts
{
    public static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    public static AccountSummary Summary(Guid id, string name) =>
        new(id, name, $"{name.ToLowerInvariant()}@exemplo.com", "owner", "44999990000", null, null, null, true, Now);
}

public sealed class RegisterOwnerAccountCommandHandlerTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();

    private readonly Mock<IOwnerRepository> _ownerRepository = new();
    private readonly Mock<IOwnersUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly RegisterOwnerAccountCommandHandler _sut;

    public RegisterOwnerAccountCommandHandlerTests()
    {
        _ownerAccounts
            .Setup(a => a.CreateAsync(It.IsAny<AccountRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountSession>.Success(
                new AccountSession("jwt", "refresh", 1_791_461_700, Accounts.Summary(AccountId, "Camila"))));

        _sut = new RegisterOwnerAccountCommandHandler(
            _ownerRepository.Object, _unitOfWork.Object, _ownerAccounts.Object, _auditTrail.Object, new FakeTimeProvider(Accounts.Now));
    }

    [Fact]
    public async Task HandleAsync_Should_CreateAccountThenOwner_When_DataIsValid()
    {
        Owner? saved = null;
        _ownerRepository.Setup(r => r.Add(It.IsAny<Owner>())).Callback<Owner>(o => saved = o);

        var result = await _sut.HandleAsync(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("jwt");
        result.Value.Owner.UserId.Should().Be(AccountId);
        result.Value.Owner.Id.Should().NotBe(AccountId, "o tutor tem id próprio");
        result.Value.Owner.Cpf.Should().Be("52998224725");
        result.Value.Owner.User!.FullName.Should().Be("Camila");

        saved!.UserId.Should().Be(AccountId);
        _ownerAccounts.Verify(
            a => a.CreateAsync(It.Is<AccountRegistration>(r => r.Role == "owner"), It.IsAny<CancellationToken>()),
            Times.Once,
            "a conta é sempre de tutor");
        _ownerAccounts.Verify(a => a.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnConflictWithoutCreatingAccount_When_CpfIsTaken()
    {
        _ownerRepository
            .Setup(r => r.ExistsByCpfAsync(It.IsAny<Cpf>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.HandleAsync(Command(), CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_CPF_ALREADY_REGISTERED");
        _ownerAccounts.Verify(a => a.CreateAsync(It.IsAny<AccountRegistration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_PropagateAccountError_When_EmailIsTaken()
    {
        var emailTaken = new Error("AUTH_EMAIL_ALREADY_REGISTERED", "taken");
        _ownerAccounts
            .Setup(a => a.CreateAsync(It.IsAny<AccountRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountSession>.Failure(emailTaken));

        var result = await _sut.HandleAsync(Command(), CancellationToken.None);

        result.FirstError.Should().Be(emailTaken);
        _ownerRepository.Verify(r => r.Add(It.IsAny<Owner>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_DeleteTheAccount_When_SavingTheOwnerFails()
    {
        // Compensação: conta sem perfil de tutor não pode sobrar.
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("banco fora do ar"));

        var act = () => _sut.HandleAsync(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _ownerAccounts.Verify(a => a.DeleteAsync(AccountId, CancellationToken.None), Times.Once);
    }

    private static RegisterOwnerAccountCommand Command() =>
        new(
            "Camila Souza",
            "camila@exemplo.com",
            "Nova@Senha123",
            "(44) 99999-0000",
            "1990-05-10",
            "529.982.247-25",
            new AddressData("87020-000", "Rua das Flores", "120", null, "Zona 7", "Maringá", "PR"));
}

public sealed class RegisterOwnerAccountCommandValidatorTests
{
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly RegisterOwnerAccountCommandValidator _sut;

    public RegisterOwnerAccountCommandValidatorTests()
    {
        _ownerAccounts
            .Setup(a => a.ValidateAsync(It.IsAny<AccountRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _sut = new RegisterOwnerAccountCommandValidator(_ownerAccounts.Object);
    }

    [Fact]
    public async Task Validate_Should_MergeCpfAndAccountErrors_When_BothAreInvalid()
    {
        _ownerAccounts
            .Setup(a => a.ValidateAsync(It.IsAny<AccountRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(
            [
                Error.Validation("password", "Password must contain at least one number."),
                Error.Validation("address.zipCode", "Zip code (CEP) must have 8 digits."),
            ]));

        var result = await _sut.ValidateAsync(Command("111.111.111-11"), TestContext.Current.CancellationToken);

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Cpf", "password", "address.zipCode"]);
    }

    [Fact]
    public async Task Validate_Should_IgnoreEmailConflict_When_AccountReportsIt()
    {
        // "E-mail já usado" é 409 e quem responde é o handler, não o validador (400).
        _ownerAccounts
            .Setup(a => a.ValidateAsync(It.IsAny<AccountRegistration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("AUTH_EMAIL_ALREADY_REGISTERED", "taken")));

        (await _sut.ValidateAsync(Command("529.982.247-25"), TestContext.Current.CancellationToken)).IsValid.Should().BeTrue();
    }

    private static RegisterOwnerAccountCommand Command(string cpf) =>
        new("Camila", "camila@exemplo.com", "Nova@Senha123", "44999990000", "1990-05-10", cpf, null);
}

public sealed class OwnerQueriesTests
{
    private readonly Mock<IOwnerRepository> _ownerRepository = new();
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();

    [Fact]
    public async Task ListOwners_Should_JoinEveryOwnerWithItsAccount_When_Called()
    {
        var first = Owner.Create(Guid.CreateVersion7(), Cpf.Create("52998224725").Value!, Accounts.Now);
        var second = Owner.Create(Guid.CreateVersion7(), Cpf.Create("11144477735").Value!, Accounts.Now);
        _ownerRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);
        _ownerAccounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Accounts.Summary(first.UserId, "Camila"), Accounts.Summary(second.UserId, "Cida")]);

        var result = await new ListOwnersQueryHandler(_ownerRepository.Object, _ownerAccounts.Object, _auditTrail.Object)
            .HandleAsync(new ListOwnersQuery(), CancellationToken.None);

        result.Value!.Select(o => o.User!.FullName).Should().Equal("Camila", "Cida");
        _ownerAccounts.Verify(
            a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "uma consulta ao Auth para a lista inteira, não uma por tutor");
    }

    [Fact]
    public async Task GetOwnerById_Should_ReturnNotFound_When_OwnerDoesNotExist()
    {
        var result = await new GetOwnerByIdQueryHandler(_ownerRepository.Object, _ownerAccounts.Object, _auditTrail.Object)
            .HandleAsync(new GetOwnerByIdQuery(Guid.CreateVersion7()), CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_NOT_FOUND");
    }
}
