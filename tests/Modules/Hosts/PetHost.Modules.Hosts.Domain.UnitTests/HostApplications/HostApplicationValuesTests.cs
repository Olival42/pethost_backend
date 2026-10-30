using FluentAssertions;
using PetHost.Modules.Hosts.Domain.HostApplications;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.HostApplications;

public sealed class HostApplicationValuesTests
{
    [Theory]
    [InlineData(HostApplicationStatus.Draft, "draft")]
    [InlineData(HostApplicationStatus.Submitted, "submitted")]
    [InlineData(HostApplicationStatus.Approved, "approved")]
    [InlineData(HostApplicationStatus.Rejected, "rejected")]
    [InlineData(HostApplicationStatus.RejectedFinal, "rejected_final")]
    [InlineData(HostApplicationStatus.Cancelled, "cancelled")]
    [InlineData(HostApplicationStatus.Expired, "expired")]
    public void Status_Should_RoundTrip_When_Converted(HostApplicationStatus status, string wire)
    {
        // Act / Assert
        HostApplicationStatusValues.ToWire(status).Should().Be(wire);
        HostApplicationStatusValues.FromWire(wire).Should().Be(status);
    }

    [Theory]
    [InlineData(IdentityDocumentType.Rg, "rg")]
    [InlineData(IdentityDocumentType.Cnh, "cnh")]
    [InlineData(IdentityDocumentType.Cin, "cin")]
    public void DocumentType_Should_RoundTrip_When_Converted(IdentityDocumentType type, string wire)
    {
        // Act / Assert
        IdentityDocumentTypeValues.ToWire(type).Should().Be(wire);
        IdentityDocumentTypeValues.FromWire(wire).Should().Be(type);
    }

    [Theory]
    [InlineData(DocumentKind.Front, "front")]
    [InlineData(DocumentKind.Back, "back")]
    [InlineData(DocumentKind.Selfie, "selfie")]
    public void DocumentKind_Should_RoundTrip_When_Converted(DocumentKind kind, string wire)
    {
        // Act / Assert
        DocumentKindValues.ToWire(kind).Should().Be(wire);
        DocumentKindValues.FromWire(wire).Should().Be(kind);
    }

    [Theory]
    [InlineData(RejectionReason.DocumentIllegible, "document_illegible")]
    [InlineData(RejectionReason.DocumentExpired, "document_expired")]
    [InlineData(RejectionReason.SelfieMismatch, "selfie_mismatch")]
    [InlineData(RejectionReason.DataMismatch, "data_mismatch")]
    [InlineData(RejectionReason.MissingDocument, "missing_document")]
    [InlineData(RejectionReason.SuspectedFraud, "suspected_fraud")]
    [InlineData(RejectionReason.Other, "other")]
    public void RejectionReason_Should_RoundTrip_When_Converted(RejectionReason reason, string wire)
    {
        // Act / Assert
        RejectionReasonValues.ToWire(reason).Should().Be(wire);
        RejectionReasonValues.FromWire(wire).Should().Be(reason);
    }

    [Fact]
    public void TryParse_Should_AcceptTrimmedCaseInsensitiveValues_When_ParsingClientInput()
    {
        // Act
        var parsed = IdentityDocumentTypeValues.TryParse(" CNH ", out var type);

        // Assert
        parsed.Should().BeTrue();
        type.Should().Be(IdentityDocumentType.Cnh);
    }

    [Theory]
    [InlineData("passport")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_Should_Refuse_When_ValueIsUnknown(string? input)
    {
        // Act / Assert
        IdentityDocumentTypeValues.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void FromWire_Should_Throw_When_ValueIsUnknown()
    {
        // Act
        var act = () => HostApplicationStatusValues.FromWire("archived");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Required_Should_ListTheThreePhotos_When_Read()
    {
        // Act / Assert
        DocumentKindValues.Required.Should().Equal(DocumentKind.Front, DocumentKind.Back, DocumentKind.Selfie);
    }
}
