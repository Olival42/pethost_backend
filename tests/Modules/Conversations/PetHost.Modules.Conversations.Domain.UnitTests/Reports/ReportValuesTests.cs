using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Reports;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Reports;

/// <summary>Os textos de motivo e situação são contrato com o app e com o banco (CHECK).</summary>
public sealed class ReportValuesTests
{
    [Theory]
    [InlineData(ReportReason.Spam, "spam")]
    [InlineData(ReportReason.Harassment, "harassment")]
    [InlineData(ReportReason.Scam, "scam")]
    [InlineData(ReportReason.Inappropriate, "inappropriate")]
    [InlineData(ReportReason.Other, "other")]
    public void ReportReason_Should_RoundTrip_When_ConvertedToAndFromText(ReportReason reason, string text)
    {
        // Act
        var wire = ReportReasonValues.ToWire(reason);
        var back = ReportReasonValues.FromWire(text);

        // Assert
        wire.Should().Be(text);
        back.Should().Be(reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SPAM")]
    [InlineData("fraude")]
    public void ReportReason_Should_NotParse_When_TheTextIsNotInTheList(string? text)
    {
        // Act
        var parsed = ReportReasonValues.TryFromWire(text, out _);

        // Assert
        parsed.Should().BeFalse();
    }

    [Fact]
    public void ReportReason_Should_Throw_When_FromWireGetsUnknownText()
    {
        // Act
        var parse = () => ReportReasonValues.FromWire("fraude");

        // Assert
        parse.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(ReportStatus.Pending, "pending")]
    [InlineData(ReportStatus.Upheld, "upheld")]
    [InlineData(ReportStatus.Dismissed, "dismissed")]
    public void ReportStatus_Should_RoundTrip_When_ConvertedToAndFromText(ReportStatus status, string text)
    {
        // Act
        var wire = ReportStatusValues.ToWire(status);
        var back = ReportStatusValues.FromWire(text);

        // Assert
        wire.Should().Be(text);
        back.Should().Be(status);
    }

    [Theory]
    [InlineData("upheld", true)]
    [InlineData("dismissed", true)]
    [InlineData("pending", false)]
    [InlineData("all", false)]
    [InlineData(null, false)]
    public void TryOutcomeFromWire_Should_AcceptOnlyTheTwoOutcomes_When_Parsing(string? text, bool expected)
    {
        // Act
        var parsed = ReportStatusValues.TryOutcomeFromWire(text, out _);

        // Assert
        parsed.Should().Be(expected);
    }

    [Fact]
    public void ReportStatus_Should_Throw_When_FromWireGetsUnknownText()
    {
        // Act
        var parse = () => ReportStatusValues.FromWire("closed");

        // Assert
        parse.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MessageReportId_Should_GenerateAVersion7IdAndPrintTheGuid_When_Created()
    {
        // Act
        var id = MessageReportId.New();

        // Assert
        id.Value.Version.Should().Be(7);
        id.ToString().Should().Be(id.Value.ToString());
    }
}
