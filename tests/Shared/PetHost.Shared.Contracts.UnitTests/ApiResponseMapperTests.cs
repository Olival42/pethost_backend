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
            Error.Validation("email", "AUTH_EMAIL_REQUIRED", "Email is required."),
            Error.Validation("email", "AUTH_EMAIL_INVALID_FORMAT", "Email format is invalid."),
            Error.Validation("role", "AUTH_ROLE_REQUIRED", "Role is required."),
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
    public void ToApiResponse_Should_AlignErrorsWithMessages_When_FieldHasMoreThanOneFailure()
    {
        var result = Result<string>.Failure([
            Error.Validation("email", "AUTH_EMAIL_REQUIRED", "Email is required."),
            Error.Validation("email", "AUTH_EMAIL_TOO_LONG", "Email must be at most 254 characters.", ("max", 254)),
        ]);

        var details = result.ToApiResponse().Error!.Details.Should().BeAssignableTo<List<DataErrors>>().Subject;

        var email = details.Should().ContainSingle().Subject;
        email.Messages.Should().Equal("Email is required.", "Email must be at most 254 characters.");
        email.Errors.Select(e => e.Code).Should().Equal("AUTH_EMAIL_REQUIRED", "AUTH_EMAIL_TOO_LONG");
        email.Errors[0].Params.Should().BeNull();
        email.Errors[1].Params.Should().BeEquivalentTo(new Dictionary<string, object> { ["max"] = 254 });
        email.Errors.Should().HaveSameCount(email.Messages);
    }

    [Fact]
    public void ToApiResponse_Should_Throw_When_ValidationErrorHasNoReason()
    {
        var result = Result<string>.Failure(new Error(ErrorCodes.Validation, "Something is off.", "name"));

        var act = () => result.ToApiResponse();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no reason code*");
    }

    [Fact]
    public void ToApiResponse_Should_DiscardNonValidationErrors_When_ValidationErrorsExist()
    {
        // Comportamento normativo da secao 7: havendo >=1 VALIDATION_ERROR, a
        // resposta e VALIDATION_ERROR e os erros de outro tipo sao descartados.
        var result = Result<string>.Failure([
            new Error("BOOKING_OVERLAPPING_PERIOD", "Overlapping."),
            Error.Validation("email", "AUTH_EMAIL_REQUIRED", "Email is required."),
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
        var result = Result<string>.Failure(new Error(ErrorCodes.Validation, "Something is off.", Reason: "TEST_SOMETHING_OFF"));

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
