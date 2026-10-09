using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Users.RegisterAccount;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Accounts;
using PetHost.TestKit;
using PetHost.Shared.Contracts.Audit;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Users;

internal static class Registration
{
    public static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    public static readonly AddressData Address =
        new("87020-000", "Rua das Flores", "120", null, "Zona 7", "Maringá", "PR");

    public static RegisterAccountCommand Valid(
        string? password = "Nova@Senha123",
        DateOnly? birthDate = null,
        AddressData? address = null,
        string role = "owner") =>
        new("Camila Souza", "camila@exemplo.com", password, role, "(44) 99999-0000",
            (birthDate ?? new DateOnly(1990, 5, 10)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            address ?? Address);
}

public sealed class RegisterAccountCommandValidatorTests
{
    private readonly RegisterAccountCommandValidator _sut = new(new FakeTimeProvider(Registration.Now));

    [Fact]
    public void Validate_Should_Pass_When_CommandIsComplete()
    {
        _sut.Validate(Registration.Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_AccumulateEveryField_When_CommandIsEmpty()
    {
        var result = _sut.Validate(new RegisterAccountCommand(null, null, null, null, null, null, null));

        result.Errors.Select(e => e.PropertyName).Should()
            .Contain(["FullName", "Email", "Password", "Role", "Phone", "BirthDate", "Address"]);
    }

    [Fact]
    public void Validate_Should_ReportNestedAddressFields_When_AddressIsInvalid()
    {
        var result = _sut.Validate(Registration.Valid(address: new AddressData("123", "", "1", null, "Zona 7", "Maringá", "XX")));

        result.Errors.Select(e => e.PropertyName).Should()
            .Contain(["address.zipCode", "address.street", "address.state"]);
    }

    [Fact]
    public void Validate_Should_Fail_When_PersonIsUnder18()
    {
        var seventeen = DateOnly.FromDateTime(Registration.Now.UtcDateTime).AddYears(-17);

        var result = _sut.Validate(Registration.Valid(birthDate: seventeen));

        result.Errors.Should().Contain(e => e.PropertyName == "BirthDate" && e.ErrorMessage.Contains("18"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("10/05/1990")]
    [InlineData("1990-13-40")]
    [InlineData("ontem")]
    public void Validate_Should_ReportBirthDateField_When_DateIsEmptyOrMalformed(string birthDate)
    {
        var command = Registration.Valid() with { BirthDate = birthDate };

        _sut.Validate(command).Errors.Should().ContainSingle(e => e.PropertyName == "BirthDate");
    }

    [Fact]
    public void Validate_Should_RequireStrongPassword_When_Registering()
    {
        var result = _sut.Validate(Registration.Valid(password: "fraca"));

        result.Errors.Where(e => e.PropertyName == "Password").Should().HaveCount(4);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("gerente")]
    public void Validate_Should_Fail_When_RoleIsNotOwnerOrHost(string role)
    {
        _sut.Validate(Registration.Valid(role: role)).Errors.Should().Contain(e => e.PropertyName == "Role");
    }
}

public sealed class RegisterAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IAuthUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IAccessTokenGenerator> _accessTokenGenerator = new();
    private readonly Mock<IRefreshTokenStore> _refreshTokenStore = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly RegisterAccountCommandHandler _sut;

    public RegisterAccountCommandHandlerTests()
    {
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns(UserBuilder.SampleHash);
        _accessTokenGenerator
            .Setup(g => g.Generate(It.IsAny<User>()))
            .Returns(new AccessToken("jwt", Registration.Now.AddMinutes(15), 900));
        _refreshTokenStore
            .Setup(s => s.IssueAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken("refresh", Registration.Now.AddDays(30)));

        _sut = new RegisterAccountCommandHandler(
            _userRepository.Object,
            _unitOfWork.Object,
            _passwordHasher.Object,
            _accessTokenGenerator.Object,
            _refreshTokenStore.Object,
            _auditTrail.Object,
            new FakeTimeProvider(Registration.Now));
    }

    [Fact]
    public async Task HandleAsync_Should_CreateAccountAndOpenSession_When_DataIsValid()
    {
        User? saved = null;
        _userRepository.Setup(r => r.Add(It.IsAny<User>())).Callback<User>(u => saved = u);

        var result = await _sut.HandleAsync(Registration.Valid(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("jwt");
        result.Value.RefreshToken.Should().Be("refresh");
        result.Value.User.Role.Should().Be("owner");
        result.Value.User.Phone.Should().Be("44999990000");
        result.Value.User.Address!.ZipCode.Should().Be("87020000");

        saved.Should().NotBeNull();
        saved!.IsActive.Should().BeTrue();
        _passwordHasher.Verify(h => h.Hash("Nova@Senha123"), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnConflict_When_EmailAlreadyHasThisRole()
    {
        _userRepository
            .Setup(r => r.ExistsByEmailAndRoleAsync(It.IsAny<Email>(), UserRole.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.HandleAsync(Registration.Valid(), CancellationToken.None);

        result.FirstError!.Code.Should().Be("AUTH_EMAIL_ALREADY_REGISTERED");
        _userRepository.Verify(r => r.Add(It.IsAny<User>()), Times.Never);
    }
}
