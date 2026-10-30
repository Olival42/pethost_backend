using FluentAssertions;
using PetHost.Modules.Conversations.Application.Reports.ReportMessage;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Reports.ReportMessage;

public sealed class ReportMessageCommandValidatorTests
{
    private readonly ReportMessageCommandValidator _sut = new();

    [Theory]
    [InlineData("spam", null)]
    [InlineData("harassment", "Me xingou.")]
    [InlineData("other", "Mandou foto de outro pet como se fosse o meu.")]
    public void Validate_Should_Pass_When_TheReportIsValid(string reason, string? description)
    {
        // Act
        var result = _sut.Validate(Command(reason, description));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnBothIdsRequired_When_TheRouteIdsAreEmpty()
    {
        // Act
        var result = _sut.Validate(new ReportMessageCommand(TestIds.Of(1), Roles.Owner, Guid.Empty, Guid.Empty, "spam", null));

        // Assert
        result.Errors.Select(e => e.ErrorMessage).Should().BeEquivalentTo(
            [ConversationsErrors.ConversationIdRequired.Message, ConversationsErrors.MessageIdRequired.Message]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Spam")]
    [InlineData("fraude")]
    public void Validate_Should_ReturnReasonInvalid_When_TheReasonIsNotInTheList(string? reason)
    {
        // Act
        var result = _sut.Validate(Command(reason, null));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Reason" && e.ErrorMessage == ConversationsErrors.ReasonInvalid.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Validate_Should_ReturnDescriptionRequired_When_TheReasonIsOtherWithoutDescription(string? description)
    {
        // Act
        var result = _sut.Validate(Command("other", description));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Description" && e.ErrorMessage == ConversationsErrors.DescriptionRequired.Message);
    }

    [Fact]
    public void Validate_Should_ReturnDescriptionTooLong_When_TheDescriptionIsLongerThanTheLimit()
    {
        // Act
        var result = _sut.Validate(Command("spam", new string('a', MessageReport.DescriptionMaxLength + 1)));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ConversationsErrors.DescriptionTooLong.Message);
    }

    [Fact]
    public void Validate_Should_AcceptTheLimit_When_TheExtraLengthIsOnlySpacesAtTheEnds()
    {
        // Act
        var result = _sut.Validate(Command("spam", "  " + new string('a', MessageReport.DescriptionMaxLength) + "  "));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnDescriptionInvalidCharacters_When_TheDescriptionHasANullCharacter()
    {
        // Act
        var result = _sut.Validate(Command("spam", "texto\0"));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ConversationsErrors.DescriptionInvalidCharacters.Message);
    }

    private static ReportMessageCommand Command(string? reason, string? description) =>
        new(TestIds.Of(1), Roles.Owner, TestIds.Of(2), TestIds.Of(3), reason, description);
}
