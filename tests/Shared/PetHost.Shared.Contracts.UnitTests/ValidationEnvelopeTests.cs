using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentValidation;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.Shared.Contracts.UnitTests;

/// <summary>O envelope de validação como o front o recebe: <c>messages</c> igual e <c>errors</c> alinhado.</summary>
public sealed class ValidationEnvelopeTests
{
    // As mesmas opções do host (Program.cs): camelCase e sem campos nulos.
    private static readonly JsonSerializerOptions HostJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Envelope_Should_SerializeMessagesAndAlignedErrorsWithParams()
    {
        var result = Result<string>.Failure([
            Error.Validation(
                "checkIn",
                "BOOKING_CHECK_IN_TOO_SOON",
                "Check-in must be at least 72 hours ahead.",
                ("minimumHours", 72),
                ("responseHours", 24),
                ("acceptHours", 48)),
            Error.Validation("careTermsAccepted", "BOOKING_CARE_TERMS_NOT_ACCEPTED", "Confirm that you will bring the pet's food."),
        ]);

        var json = JsonNode.Parse(JsonSerializer.Serialize(result.ToApiResponse(), HostJson))!;

        json["success"]!.GetValue<bool>().Should().BeFalse();
        json["error"]!["code"]!.GetValue<string>().Should().Be("VALIDATION_ERROR");

        var details = json["error"]!["details"]!.AsArray();
        details.Should().HaveCount(2);

        var checkIn = details.Single(d => d!["field"]!.GetValue<string>() == "checkIn")!;
        checkIn["messages"]![0]!.GetValue<string>().Should().Be("Check-in must be at least 72 hours ahead.");
        checkIn["errors"]![0]!["code"]!.GetValue<string>().Should().Be("BOOKING_CHECK_IN_TOO_SOON");
        checkIn["errors"]![0]!["params"]!["minimumHours"]!.GetValue<int>().Should().Be(72);
        checkIn["errors"]![0]!["params"]!["responseHours"]!.GetValue<int>().Should().Be(24);
        checkIn["errors"]![0]!["params"]!["acceptHours"]!.GetValue<int>().Should().Be(48);

        var terms = details.Single(d => d!["field"]!.GetValue<string>() == "careTermsAccepted")!;
        terms["errors"]![0]!["code"]!.GetValue<string>().Should().Be("BOOKING_CARE_TERMS_NOT_ACCEPTED");
        terms["errors"]![0]!.AsObject().ContainsKey("params").Should().BeFalse("a rule without parameters has no params key");
    }

    [Fact]
    public void Envelope_Should_KeepErrorsInTheSameOrderAndSizeAsMessages()
    {
        var result = Result<string>.Failure([
            Error.Validation("password", "AUTH_PASSWORD_TOO_SHORT", "Too short.", ("min", 8)),
            Error.Validation("password", "AUTH_PASSWORD_MISSING_DIGIT", "Needs a number."),
            Error.Validation("password", "AUTH_PASSWORD_MISSING_UPPERCASE", "Needs an uppercase letter."),
        ]);

        var json = JsonNode.Parse(JsonSerializer.Serialize(result.ToApiResponse(), HostJson))!;
        var password = json["error"]!["details"]![0]!;

        password["messages"]!.AsArray().Select(m => m!.GetValue<string>()).Should().Equal("Too short.", "Needs a number.", "Needs an uppercase letter.");
        password["errors"]!.AsArray().Select(e => e!["code"]!.GetValue<string>())
            .Should().Equal("AUTH_PASSWORD_TOO_SHORT", "AUTH_PASSWORD_MISSING_DIGIT", "AUTH_PASSWORD_MISSING_UPPERCASE");
    }

    [Fact]
    public void Envelope_Should_NotCarryErrorsOrParams_ForNonValidationFailures()
    {
        var result = Result<string>.Failure(new Error("BOOKING_CAPACITY_EXCEEDED", "No room."));

        var json = JsonNode.Parse(JsonSerializer.Serialize(result.ToApiResponse(), HostJson))!;

        json["error"]!["code"]!.GetValue<string>().Should().Be("BOOKING_CAPACITY_EXCEEDED");
        json["error"]!.AsObject().ContainsKey("details").Should().BeFalse();
    }
}

public sealed class ValidationErrorExtensionsTests
{
    private static readonly Error NameRequired = Error.Validation("name", "SAMPLE_NAME_REQUIRED", "Name is required.");
    private static readonly Error NameTooLong = Error.Validation("name", "SAMPLE_NAME_TOO_LONG", "Name must be at most 5 characters.", ("max", 5));

    private sealed record Input(string? Name, Address? Address);

    private sealed record Address(string? City);

    private sealed class InputValidator : AbstractValidator<Input>
    {
        public InputValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithError(NameRequired);
            RuleFor(x => x.Name).MaximumLength(5).WithError(NameTooLong);
            RuleFor(x => x.Address).CustomErrors((address, context) =>
            {
                if (address is { City: null })
                    context.AddFailure(Error.Validation("address.city", "SAMPLE_CITY_REQUIRED", "City is required."));
            });
        }
    }

    [Fact]
    public async Task WithError_Should_SetMessageErrorCodeAndState()
    {
        var result = await new InputValidator().ValidateAsync(new Input("too long name", null), TestContext.Current.CancellationToken);

        var failure = result.Errors.Should().ContainSingle().Subject;
        failure.ErrorMessage.Should().Be("Name must be at most 5 characters.");
        failure.ErrorCode.Should().Be("SAMPLE_NAME_TOO_LONG");
        failure.CustomState.Should().Be(NameTooLong);
        failure.PropertyName.Should().Be("Name");
    }

    [Fact]
    public async Task AddFailure_Should_UseTheFieldAndCodeOfTheError()
    {
        var result = await new InputValidator().ValidateAsync(new Input("ok", new Address(null)), TestContext.Current.CancellationToken);

        var failure = result.Errors.Should().ContainSingle().Subject;
        failure.PropertyName.Should().Be("address.city");
        failure.ErrorCode.Should().Be("SAMPLE_CITY_REQUIRED");
        failure.ErrorMessage.Should().Be("City is required.");
    }

    [Fact]
    public void WithError_Should_Reject_When_ErrorIsNotAValidationErrorWithReason()
    {
        var rule = new InlineValidator<Input>();

        var act = () => rule.RuleFor(x => x.Name).NotEmpty().WithError(new Error("SAMPLE_BUSY", "Busy."));

        act.Should().Throw<ArgumentException>();
    }
}
