using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests;

public sealed record SampleCommand(string? Email, string? Nested) : ICommand<string>;

public static class SampleErrors
{
    public static readonly Error EmailRequired = Error.Validation("email", "SAMPLE_EMAIL_REQUIRED", "Email is required.");

    public static readonly Error EmailTooLong = Error.Validation("email", "SAMPLE_EMAIL_TOO_LONG", "Email must be at most 5 characters.", ("max", 5));

    public static readonly Error NestedRequired = Error.Validation("nested", "SAMPLE_NESTED_REQUIRED", "Nested is required.");
}

public sealed class SampleCommandValidator : AbstractValidator<SampleCommand>
{
    public SampleCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithError(SampleErrors.EmailRequired);
        RuleFor(x => x.Nested).NotEmpty().WithError(SampleErrors.NestedRequired);
    }
}

/// <summary>Validador com uma regra sem código estável (bug de quem escreveu): o decorator não deixa passar.</summary>
public sealed class UncodedCommandValidator : AbstractValidator<SampleCommand>
{
    public UncodedCommandValidator() => RuleFor(x => x.Email).NotEmpty();
}

/// <summary>Validador com duas regras no mesmo campo e uma de código livre (<c>CustomErrors</c>).</summary>
public sealed class ParametrizedCommandValidator : AbstractValidator<SampleCommand>
{
    public ParametrizedCommandValidator()
    {
        RuleFor(x => x.Email).MaximumLength(5).WithError(SampleErrors.EmailTooLong);
        RuleFor(x => x.Nested).CustomErrors((nested, context) =>
        {
            if (nested is null)
                context.AddFailure(SampleErrors.NestedRequired);
        });
    }
}

public sealed class ValidationCommandHandlerDecoratorTests
{
    private readonly Mock<ICommandHandler<SampleCommand, string>> _inner = new();

    public ValidationCommandHandlerDecoratorTests() =>
        _inner
            .Setup(h => h.HandleAsync(It.IsAny<SampleCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("executou"));

    [Fact]
    public async Task HandleAsync_Should_CallInnerHandler_When_CommandIsValid()
    {
        var sut = Build(new SampleCommandValidator());

        var result = await sut.HandleAsync(new SampleCommand("a@b.com", "x"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("executou");
        _inner.Verify(
            h => h.HandleAsync(It.IsAny<SampleCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotCallInnerHandler_When_CommandIsInvalid()
    {
        // A secao 10 diz que o decorator retorna antes de executar o caso de uso.
        var sut = Build(new SampleCommandValidator());

        await sut.HandleAsync(new SampleCommand(null, null), CancellationToken.None);

        _inner.Verify(
            h => h.HandleAsync(It.IsAny<SampleCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_AccumulateEveryFailure_When_CommandIsInvalid()
    {
        var sut = Build(new SampleCommandValidator());

        var result = await sut.HandleAsync(new SampleCommand(null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().HaveCount(2);
        result.Errors!.Should().OnlyContain(e => e.Code == ErrorCodes.Validation);
    }

    [Fact]
    public async Task HandleAsync_Should_ConvertFieldToCamelCase_When_ReportingFailures()
    {
        // O Field tem de casar com o campo no JSON da request (secao 10).
        var sut = Build(new SampleCommandValidator());

        var result = await sut.HandleAsync(new SampleCommand(null, null), CancellationToken.None);

        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(["email", "nested"]);
    }

    [Fact]
    public async Task HandleAsync_Should_CarryTheStableReasonOfEachRule_When_CommandIsInvalid()
    {
        var sut = Build(new SampleCommandValidator());

        var result = await sut.HandleAsync(new SampleCommand(null, null), CancellationToken.None);

        result.Errors!.Select(e => e.Reason).Should().BeEquivalentTo(["SAMPLE_EMAIL_REQUIRED", "SAMPLE_NESTED_REQUIRED"]);
        result.Errors!.Should().OnlyContain(e => e.Code == ErrorCodes.Validation && e.Params == null);
    }

    [Fact]
    public async Task HandleAsync_Should_CarryTheParamsAndTheMessage_When_RuleDeclaresThem()
    {
        var sut = Build(new ParametrizedCommandValidator());

        var result = await sut.HandleAsync(new SampleCommand("too long email", null), CancellationToken.None);

        var tooLong = result.Errors!.Single(e => e.Reason == "SAMPLE_EMAIL_TOO_LONG");
        tooLong.Field.Should().Be("email");
        tooLong.Message.Should().Be("Email must be at most 5 characters.");
        tooLong.Params!.Values.Should().BeEquivalentTo(new Dictionary<string, object> { ["max"] = 5 });
    }

    [Fact]
    public async Task HandleAsync_Should_KeepTheCodeOfFailuresAddedByCustomErrors()
    {
        var sut = Build(new ParametrizedCommandValidator());

        var result = await sut.HandleAsync(new SampleCommand("ok", null), CancellationToken.None);

        var error = result.Errors.Should().ContainSingle().Subject;
        error.Reason.Should().Be("SAMPLE_NESTED_REQUIRED");
        error.Field.Should().Be("nested");
    }

    [Fact]
    public async Task HandleAsync_Should_Throw_When_ARuleHasNoStableCode()
    {
        var sut = Build(new UncodedCommandValidator());

        var act = () => sut.HandleAsync(new SampleCommand(null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*no stable error code*");
    }

    [Fact]
    public async Task HandleAsync_Should_CallInnerHandler_When_NoValidatorIsRegistered()
    {
        var sut = Build();

        var result = await sut.HandleAsync(new SampleCommand(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private ValidationCommandHandlerDecorator<SampleCommand, string> Build(
        params IValidator<SampleCommand>[] validators) =>
        new(_inner.Object, validators);
}

public sealed class ErrorCodeStatusMapperTests
{
    [Theory]
    [InlineData(ErrorCodes.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorCodes.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorCodes.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorCodes.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorCodes.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorCodes.BusinessRule, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(ErrorCodes.Unexpected, StatusCodes.Status500InternalServerError)]
    public void ToHttpStatusCode_Should_MatchCatalog_When_CodeIsTransversal(string code, int expected)
    {
        code.ToHttpStatusCode().Should().Be(expected);
    }

    [Theory]
    [InlineData("BOOKING_NOT_FOUND", StatusCodes.Status404NotFound)]
    [InlineData("AUTH_USER_NOT_FOUND", StatusCodes.Status404NotFound)]
    [InlineData("PAYMENT_CONFLICT", StatusCodes.Status409Conflict)]
    [InlineData("AUTH_EMAIL_ALREADY_REGISTERED", StatusCodes.Status409Conflict)]
    [InlineData("AUTH_INVALID_CREDENTIALS", StatusCodes.Status401Unauthorized)]
    [InlineData("AUTH_REFRESH_TOKEN_INVALID", StatusCodes.Status401Unauthorized)]
    [InlineData("AUTH_ADMIN_REGISTRATION_FORBIDDEN", StatusCodes.Status403Forbidden)]
    [InlineData("PET_NOT_OWNED_BY_REQUESTER", StatusCodes.Status403Forbidden)]
    [InlineData("LISTING_GEOCODER_UNAVAILABLE", StatusCodes.Status503ServiceUnavailable)]
    public void ToHttpStatusCode_Should_InferFromSuffix_When_CodeIsFromAModule(string code, int expected)
    {
        code.ToHttpStatusCode().Should().Be(expected);
    }

    [Theory]
    [InlineData("BOOKING_OVERLAPPING_PERIOD")]
    [InlineData("AUTH_PASSWORD_HASH_INVALID")]
    public void ToHttpStatusCode_Should_FallBackTo422_When_SuffixIsUnknown(string code)
    {
        // Fallback da secao 7: regra de negocio, nao falha do servidor.
        code.ToHttpStatusCode().Should().Be(StatusCodes.Status422UnprocessableEntity);
    }
}

public sealed class ResultActionResultExtensionsTests
{
    [Fact]
    public void ToActionResult_Should_UseSuccessStatus_When_ResultIsSuccess()
    {
        var action = Result<string>.Success("ok").ToActionResult(StatusCodes.Status201Created);

        var objectResult = action.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        objectResult.Value.Should().BeOfType<ApiResponse<string>>()
            .Which.Success.Should().BeTrue();
    }

    [Fact]
    public void ToActionResult_Should_DeriveStatusFromErrorCode_When_ResultIsFailure()
    {
        var result = Result<string>.Failure(new Error("AUTH_INVALID_CREDENTIALS", "Nope."));

        var action = result.ToActionResult(StatusCodes.Status201Created);

        // O status de sucesso e ignorado na falha: quem decide e o codigo do erro.
        action.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void ToActionResult_Should_Return400_When_ResultHasValidationErrors()
    {
        var result = Result<string>.Failure(Error.Validation("email", "AUTH_EMAIL_REQUIRED", "Email is required."));

        result.ToActionResult().Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
