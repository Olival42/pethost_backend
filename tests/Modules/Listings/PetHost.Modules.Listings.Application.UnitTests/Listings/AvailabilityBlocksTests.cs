using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Listings.Application.Listings.AvailabilityBlocks;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

/// <summary>Agenda do cantinho: bloquear, reabrir e ler as datas fechadas.</summary>
public sealed class AvailabilityBlocksTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    private readonly Mock<IListingRepository> _listings = new();
    private readonly Mock<IAvailabilityBlockRepository> _blocks = new();
    private readonly Mock<IListingsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly FakeTimeProvider _time = new(ListingBuilder.DefaultNow);
    private readonly Listing _listing = ListingsTestData.IndividualListing();
    private readonly List<AvailabilityBlock> _open = [];

    public AvailabilityBlocksTests()
    {
        _listings.Setup(r => r.GetByIdAsync(_listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_listing);
        _blocks.Setup(r => r.ListOpenAsync(_listing.Id, Today, It.IsAny<CancellationToken>())).ReturnsAsync(() => [.. _open]);
    }

    // ----- bloquear -----

    [Fact]
    public async Task Block_Should_SaveAndRecordThePeriodWithoutTheReason_When_TheOwnerBlocks()
    {
        // Act
        var result = await Block().HandleAsync(Command("2026-12-20", "2026-12-26", "Viagem de fim de ano"), CancellationToken.None);

        // Assert
        result.Value!.StartDate.Should().Be(new DateOnly(2026, 12, 20));
        result.Value.Days.Should().Be(7);
        result.Value.Reason.Should().Be("Viagem de fim de ano");
        _blocks.Verify(r => r.Add(It.Is<AvailabilityBlock>(b => b.ListingId == _listing.Id)), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.ListingDatesBlocked
                    && r.TargetId == _listing.Id.Value
                    && r.Details!["startDate"] == "2026-12-20"
                    && r.Details["endDate"] == "2026-12-26"
                    && r.Reason == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Block_Should_FailWithOverlapWithoutSaving_When_TheDatesAreAlreadyBlocked()
    {
        // Arrange
        _open.Add(AvailabilityBlock.Create(_listing.Id, new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 31), null, [], ListingBuilder.DefaultNow).Value!);

        // Act
        var result = await Block().HandleAsync(Command("2026-12-20", "2026-12-26"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("LISTING_AVAILABILITY_BLOCK_OVERLAP");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Block_Should_ReturnNotFound_When_TheListingIsFromAnotherHost()
    {
        // Act
        var result = await Block().HandleAsync(
            new BlockListingDatesCommand(ListingsTestData.CompanyUserId, _listing.Id.Value, "2026-12-20", "2026-12-26", null),
            CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("LISTING_NOT_FOUND");
    }

    [Fact]
    public async Task Block_Should_ReturnHostDeactivated_When_TheHostIsSuspended()
    {
        // Act
        var result = await Block().HandleAsync(
            new BlockListingDatesCommand(ListingsTestData.SuspendedUserId, _listing.Id.Value, "2026-12-20", "2026-12-26", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.HostDeactivated);
    }

    [Fact]
    public void BlockValidator_Should_ReturnEveryFieldError_When_TheBodyIsInvalid()
    {
        // Act
        var result = new BlockListingDatesCommandValidator(_time).Validate(
            new BlockListingDatesCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, "20/12/2026", null, new string('x', 121)));

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["StartDate", "EndDate", "Reason"]);
    }

    [Fact]
    public void BlockValidator_Should_StillCheckTheStart_When_TheEndIsUnreadable()
    {
        // Act
        var result = new BlockListingDatesCommandValidator(_time).Validate(Command("2026-10-10", "31/12/2026"));

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["EndDate", "startDate"]);
    }

    [Fact]
    public void BlockValidator_Should_ApplyTheDateRules_When_TheFormatIsValid()
    {
        // Act
        var result = new BlockListingDatesCommandValidator(_time).Validate(Command("2026-10-10", "2026-10-09"));

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["startDate", "endDate"]);
    }

    // ----- reabrir -----

    [Fact]
    public async Task Unblock_Should_RemoveAndReturnTheRest_When_TheBlockIsFromTheListing()
    {
        // Arrange
        var block = AvailabilityBlock.Create(_listing.Id, new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 26), null, [], ListingBuilder.DefaultNow).Value!;
        _blocks.Setup(r => r.GetAsync(_listing.Id, block.Id, It.IsAny<CancellationToken>())).ReturnsAsync(block);

        // Act
        var result = await Unblock().HandleAsync(
            new UnblockListingDatesCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, block.Id.Value),
            CancellationToken.None);

        // Assert
        result.Value.Should().BeEmpty();
        _blocks.Verify(r => r.Remove(block), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ListingDatesUnblocked), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unblock_Should_ReturnBlockNotFound_When_TheBlockIsNotFromTheListing()
    {
        // Act
        var result = await Unblock().HandleAsync(
            new UnblockListingDatesCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, Guid.CreateVersion7()),
            CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("LISTING_AVAILABILITY_BLOCK_NOT_FOUND");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- ler -----

    [Fact]
    public async Task List_Should_ShowTheReasonOnlyToTheOwnerAndTheAdmin_When_Read()
    {
        // Arrange
        _open.Add(AvailabilityBlock.Create(_listing.Id, new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 26), "Viagem", [], ListingBuilder.DefaultNow).Value!);

        // Act
        var owner = await List().HandleAsync(new ListAvailabilityBlocksQuery(ListingsTestData.IndividualUserId, Roles.Host, _listing.Id.Value), CancellationToken.None);
        var admin = await List().HandleAsync(new ListAvailabilityBlocksQuery(Guid.CreateVersion7(), Roles.Admin, _listing.Id.Value), CancellationToken.None);
        var tutor = await List().HandleAsync(new ListAvailabilityBlocksQuery(ListingsTestData.OwnerUserId, Roles.Owner, _listing.Id.Value), CancellationToken.None);

        // Assert
        owner.Value!.Single().Reason.Should().Be("Viagem");
        admin.Value!.Single().Reason.Should().Be("Viagem");
        tutor.Value!.Single().Reason.Should().BeNull();
        tutor.Value!.Single().EndDate.Should().Be(new DateOnly(2026, 12, 26));
    }

    [Fact]
    public async Task List_Should_ReturnNotFound_When_AnotherAccountAsksForAnInactiveListing()
    {
        // Arrange
        _listing.Deactivate(ListingBuilder.DefaultNow);

        // Act
        var result = await List().HandleAsync(new ListAvailabilityBlocksQuery(ListingsTestData.OwnerUserId, Roles.Owner, _listing.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("LISTING_NOT_FOUND");
    }

    private BlockListingDatesCommand Command(string start, string end, string? reason = null) =>
        new(ListingsTestData.IndividualUserId, _listing.Id.Value, start, end, reason);

    private BlockListingDatesCommandHandler Block() =>
        new(_listings.Object, _blocks.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _auditTrail.Object, _time);

    private UnblockListingDatesCommandHandler Unblock() =>
        new(_listings.Object, _blocks.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _auditTrail.Object, _time);

    private ListAvailabilityBlocksQueryHandler List() =>
        new(_listings.Object, _blocks.Object, ListingsTestData.Hosts().Object, _time);
}
