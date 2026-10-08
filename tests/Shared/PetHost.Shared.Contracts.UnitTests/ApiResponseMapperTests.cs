using FluentAssertions;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.Shared.Contracts.UnitTests;

public sealed class ApiResponseMapperTests
{
    [Fact]
    public void ToApiResponse_Should_WrapValue_When_ResultIsSuccess()
    {
        var response = Result<string>.Success("ok").ToApiResponse();

        response.Success.Should().BeTrue();
        response.Data.Should().Be("ok");
        response.Error.Should().BeNull();
    }

    [Fact]
    public void ToApiResponse_Should_GroupValidationErrorsByField_When_ResultHasThem()
    {
        var result = Result<string>.Failure([
            Error.Validation("email", "Email is required."),
            Error.Validation("email", "Email format is invalid."),
            Error.Validation("role", "Role is required."),
        ]);

        var response = result.ToApiResponse();

        response.Error!.Code.Should().Be(ErrorCodes.Validation);
        response.Error.Message.Should().Be("Validation failed");

        var details = response.Error.Details.Should().BeAssignableTo<List<DataErrors>>().Subject;
        details.Should().HaveCount(2);
        details.Single(d => d.Field == "email").Messages.Should().HaveCount(2);
        details.Single(d => d.Field == "role").Messages.Should().ContainSingle();
    }

    [Fact]
    public void ToApiResponse_Should_DiscardNonValidationErrors_When_ValidationErrorsExist()
    {
        // Comportamento normativo da secao 7: havendo >=1 VALIDATION_ERROR, a
        // resposta e VALIDATION_ERROR e os erros de outro tipo sao descartados.
        var result = Result<string>.Failure([
            new Error("BOOKING_OVERLAPPING_PERIOD", "Overlapping."),
            Error.Validation("email", "Email is required."),
        ]);

        var response = result.ToApiResponse();

        response.Error!.Code.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public void ToApiResponse_Should_UseFirstError_When_NoValidationErrorExists()
    {
        var result = Result<string>.Failure([
            new Error("AUTH_INVALID_CREDENTIALS", "Email or password is incorrect."),
            new Error("SECOND_ERROR", "Ignored."),
        ]);

        var response = result.ToApiResponse();

        response.Error!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
        response.Error.Details.Should().BeNull();
    }

    [Fact]
    public void ToApiResponse_Should_UseGeneralBucket_When_ValidationErrorHasNoField()
    {
        var result = Result<string>.Failure(new Error(ErrorCodes.Validation, "Something is off."));

        var response = result.ToApiResponse();

        var details = response.Error!.Details.Should().BeAssignableTo<List<DataErrors>>().Subject;
        details.Should().ContainSingle().Which.Field.Should().Be("general");
    }

    [Fact]
    public void ToApiResponse_Should_NeverCarryData_When_ResultIsFailure()
    {
        var response = Result<string>.Failure(new Error("X", "Y")).ToApiResponse();

        response.Success.Should().BeFalse();
        response.Data.Should().BeNull();
    }
}
