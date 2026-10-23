using FluentAssertions;
using PetHost.Shared.Kernel.Errors;
using Xunit;

namespace PetHost.Shared.Kernel.UnitTests.Errors;

public sealed class ErrorTests
{
    [Fact]
    public void Validation_Should_KeepTopLevelCodeAndCarryTheReason()
    {
        var error = Error.Validation("checkIn", "BOOKING_CHECK_IN_TOO_SOON", "Too soon.");

        error.Code.Should().Be(ErrorCodes.Validation);
        error.Field.Should().Be("checkIn");
        error.Reason.Should().Be("BOOKING_CHECK_IN_TOO_SOON");
        error.Message.Should().Be("Too soon.");
        error.Params.Should().BeNull();
    }

    [Fact]
    public void Validation_Should_CarryTheParams_When_TheSentenceNeedsThem()
    {
        var error = Error.Validation(
            "checkIn",
            "BOOKING_CHECK_IN_TOO_SOON",
            "Too soon.",
            ("minimumHours", 72),
            ("responseHours", 24),
            ("acceptHours", 48));

        error.Params!.Values.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["minimumHours"] = 72,
            ["responseHours"] = 24,
            ["acceptHours"] = 48,
        });
    }

    [Theory]
    [InlineData("booking_check_in_too_soon")]
    [InlineData("BOOKING")]
    [InlineData("Booking_Check_In")]
    [InlineData("BOOKING__CHECK")]
    [InlineData("BOOKING CHECK")]
    [InlineData("")]
    public void Validation_Should_Reject_When_ReasonIsNotScreamingSnakeCase(string reason)
    {
        var act = () => Error.Validation("field", reason, "Message.");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Errors_Should_BeEqualByContent_When_FieldsAndParamsMatch()
    {
        var first = Error.Validation("name", "PET_NAME_TOO_LONG", "Too long.", ("max", 100));
        var second = Error.Validation("name", "PET_NAME_TOO_LONG", "Too long.", ("max", 100));
        var other = Error.Validation("name", "PET_NAME_TOO_LONG", "Too long.", ("max", 50));

        first.Should().Be(second);
        first.Should().NotBe(other);
    }

    [Fact]
    public void NonValidationErrors_Should_HaveNoReasonNorParams()
    {
        var error = new Error("BOOKING_NOT_FOUND", "Booking was not found.");

        error.Reason.Should().BeNull();
        error.Params.Should().BeNull();
    }
}

public sealed class ErrorParamsTests
{
    [Fact]
    public void Of_Should_AcceptNumbersStringsAndBooleans()
    {
        var parameters = ErrorParams.Of(("max", 10), ("format", "yyyy-MM-dd"), ("strict", true), ("rate", 10.5m), ("big", 5_000_000_000L));

        parameters.Values.Keys.Should().Equal("big", "format", "max", "rate", "strict");
    }

    [Theory]
    [InlineData("Max")]
    [InlineData("max_value")]
    [InlineData("1max")]
    [InlineData("")]
    public void Of_Should_Reject_When_NameIsNotCamelCase(string name)
    {
        var act = () => ErrorParams.Of((name, 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Of_Should_Reject_When_ValueIsNotANumberStringOrBoolean()
    {
        // Evita vazar objeto inteiro (dado pessoal) pelo envelope.
        var act = () => ErrorParams.Of(("owner", new object()));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Of_Should_Reject_When_NameIsRepeated()
    {
        var act = () => ErrorParams.Of(("max", 1), ("max", 2));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equals_Should_IgnoreOrderOfDeclaration()
    {
        ErrorParams.Of(("min", 1), ("max", 2)).Should().Be(ErrorParams.Of(("max", 2), ("min", 1)));
    }
}

public sealed class ErrorCatalogTests
{
    private static readonly string[] MaxOnly = ["max"];

    [Fact]
    public void Discover_Should_ReadValidationReasonsAndTopLevelCodes_FromErrorsClasses()
    {
        var entries = ErrorCatalog.Discover([typeof(SampleErrors).Assembly]);

        entries.Should().Contain(e => e.Code == "SAMPLE_NAME_TOO_LONG" && e.IsValidation && e.Field == "name" && e.ParamNames.SequenceEqual(MaxOnly));
        entries.Should().Contain(e => e.Code == "SAMPLE_NOT_FOUND" && !e.IsValidation && e.HasArguments);
        entries.Should().Contain(e => e.Code == "SAMPLE_BUSY" && !e.IsValidation && !e.HasArguments);
        entries.Should().Contain(e => e.Code == ErrorCodes.Validation && e.Source == "ErrorCodes.Validation");
    }

    [Fact]
    public void Codes_Should_ListEachCodeOnceInOrder()
    {
        var entries = ErrorCatalog.Discover([typeof(SampleErrors).Assembly]);

        var codes = ErrorCatalog.Codes(entries);

        codes.Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
        codes.Should().Contain(["SAMPLE_NAME_TOO_LONG", "SAMPLE_NOT_FOUND", "VALIDATION_ERROR"]);
    }
}

public static class SampleErrors
{
    public static readonly Error NameTooLong = Error.Validation("name", "SAMPLE_NAME_TOO_LONG", "Too long.", ("max", 10));

    public static readonly Error Busy = new("SAMPLE_BUSY", "Busy.");

    public static Error NotFound(Guid id) => new("SAMPLE_NOT_FOUND", $"Sample '{id}' was not found.");
}
