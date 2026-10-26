using FluentAssertions;
using Moq;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.GetMyOwner;
using PetHost.Modules.Owners.Domain.Owners;
using Xunit;

namespace PetHost.Modules.Owners.Application.UnitTests.Owners;

public sealed class GetMyOwnerQueryHandlerTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly Mock<IOwnerRepository> _ownerRepository = new();
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly GetMyOwnerQueryHandler _sut;

    public GetMyOwnerQueryHandlerTests() => _sut = new GetMyOwnerQueryHandler(_ownerRepository.Object, _ownerAccounts.Object);

    [Fact]
    public async Task HandleAsync_Should_ReturnTheOwnerWithTheAccount_When_TheProfileExists()
    {
        // Arrange
        var owner = Owner.Create(UserId, Cpf.Create("52998224725").Value!, Accounts.Now);
        _ownerRepository.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);
        _ownerAccounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Accounts.Summary(UserId, "Camila Souza")]);

        // Act
        var result = await _sut.HandleAsync(new GetMyOwnerQuery(UserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(owner.Id.Value);
        result.Value.User!.FullName.Should().Be("Camila Souza");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFoundWithoutReadingTheAccount_When_TheAccountHasNoOwnerProfile()
    {
        // Act
        var result = await _sut.HandleAsync(new GetMyOwnerQuery(UserId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("OWNER_NOT_FOUND");
        _ownerAccounts.VerifyNoOtherCalls();
    }
}
