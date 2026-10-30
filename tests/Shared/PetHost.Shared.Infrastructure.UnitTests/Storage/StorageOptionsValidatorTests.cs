using FluentAssertions;
using PetHost.Shared.Infrastructure.Storage;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Storage;

public sealed class StorageOptionsValidatorTests
{
    [Fact]
    public void Validate_Should_Succeed_When_SignedUrlSettingsAreOmitted()
    {
        // Act
        var result = new StorageOptionsValidator().Validate(null, Valid());

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Validate_Should_Fail_When_SignedUrlLifetimeIsOutOfRange(int minutes)
    {
        // Act
        var result = new StorageOptionsValidator().Validate(null, Valid(lifetimeMinutes: minutes));

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SignedUrlLifetimeMinutes");
    }

    [Fact]
    public void Validate_Should_Fail_When_SignedUrlServiceUrlIsNotAnAbsoluteUrl()
    {
        // Act
        var result = new StorageOptionsValidator().Validate(null, Valid(signedUrlServiceUrl: "localhost:9000"));

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SignedUrlServiceUrl");
    }

    [Fact]
    public void SigningServiceUrl_Should_FallBackToServiceUrl_When_NotSet()
    {
        // Assert
        Valid().SigningServiceUrl.Should().Be("http://rustfs:9000");
        Valid(signedUrlServiceUrl: "http://localhost:9000").SigningServiceUrl.Should().Be("http://localhost:9000");
    }

    private static StorageOptions Valid(int lifetimeMinutes = 5, string? signedUrlServiceUrl = null) => new()
    {
        ServiceUrl = "http://rustfs:9000",
        Region = "us-east-1",
        AccessKeyId = "key",
        SecretAccessKey = "secret",
        BucketName = "pethost-images",
        PrivateBucketName = "pethost-documents",
        PublicBaseUrl = "http://localhost:9000/pethost-images",
        SignedUrlServiceUrl = signedUrlServiceUrl,
        SignedUrlLifetimeMinutes = lifetimeMinutes,
    };
}
