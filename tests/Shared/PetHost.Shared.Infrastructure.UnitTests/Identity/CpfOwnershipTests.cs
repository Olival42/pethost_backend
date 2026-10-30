using FluentAssertions;
using Moq;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Identity;
using PetHost.Shared.Infrastructure.Identity;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Identity;

/// <summary>Restrição 9: o mesmo CPF só em contas do mesmo e-mail (a mesma pessoa).</summary>
public sealed class CpfOwnershipTests
{
    private const string Cpf = "52998224725";

    private static readonly Guid Camila = Guid.CreateVersion7();
    private static readonly Guid CamilaHost = Guid.CreateVersion7();
    private static readonly Guid Stranger = Guid.CreateVersion7();

    private readonly Mock<ICpfHolderLookup> _owners = new();
    private readonly Mock<ICpfHolderLookup> _hosts = new();
    private readonly Mock<IUserDirectory> _directory = new();

    public CpfOwnershipTests()
    {
        _owners.Setup(l => l.FindAccountIdsAsync(Cpf, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _hosts.Setup(l => l.FindAccountIdsAsync(Cpf, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _directory
            .Setup(d => d.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => [.. ids.Select(Account).OfType<AccountSummary>()]);
    }

    private CpfOwnership Sut() => new([_owners.Object, _hosts.Object], _directory.Object);

    private static AccountSummary? Account(Guid id)
    {
        string? email = id == Camila || id == CamilaHost ? "camila@exemplo.com" : id == Stranger ? "outra@exemplo.com" : null;
        return email is null ? null : new AccountSummary(id, "X", email, "owner", null, null, null, null, true, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task IsHeldByAnotherPerson_Should_BeFalse_When_NobodyUsesTheCpf()
    {
        // Act / Assert
        (await Sut().IsHeldByAnotherPersonAsync(Cpf, "camila@exemplo.com", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task IsHeldByAnotherPerson_Should_BeFalse_When_TheSamePersonUsesItInTheOtherRole()
    {
        // Arrange: a Camila já é tutora com esse CPF e agora pede como anfitriã.
        _owners.Setup(l => l.FindAccountIdsAsync(Cpf, It.IsAny<CancellationToken>())).ReturnsAsync([Camila]);

        // Act / Assert
        (await Sut().IsHeldByAnotherPersonAsync(Cpf, CamilaHost, CancellationToken.None)).Should().BeFalse();
        (await Sut().IsHeldByAnotherPersonAsync(Cpf, " CAMILA@exemplo.com ", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task IsHeldByAnotherPerson_Should_BeTrue_When_AnotherEmailUsesTheCpf()
    {
        // Arrange
        _hosts.Setup(l => l.FindAccountIdsAsync(Cpf, It.IsAny<CancellationToken>())).ReturnsAsync([Stranger]);

        // Act / Assert
        (await Sut().IsHeldByAnotherPersonAsync(Cpf, "camila@exemplo.com", CancellationToken.None)).Should().BeTrue();
        (await Sut().IsHeldByAnotherPersonAsync(Cpf, Camila, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task IsHeldByAnotherPerson_Should_IgnoreTheRequesterOwnAccount_When_ItAlreadyHasTheCpf()
    {
        // Arrange
        _hosts.Setup(l => l.FindAccountIdsAsync(Cpf, It.IsAny<CancellationToken>())).ReturnsAsync([CamilaHost]);

        // Act / Assert
        (await Sut().IsHeldByAnotherPersonAsync(Cpf, CamilaHost, CancellationToken.None)).Should().BeFalse();
        _directory.Verify(d => d.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IsHeldByAnotherPerson_Should_IgnoreAccountsThatNoLongerExist_When_Checking()
    {
        // Arrange
        _owners.Setup(l => l.FindAccountIdsAsync(Cpf, It.IsAny<CancellationToken>())).ReturnsAsync([Guid.CreateVersion7()]);

        // Act / Assert
        (await Sut().IsHeldByAnotherPersonAsync(Cpf, "camila@exemplo.com", CancellationToken.None)).Should().BeFalse();
    }
}
