using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests;

public sealed record SampleCommand(string? Email, string? Nested) : ICommand<string>;

public sealed class SampleCommandValidator : AbstractValidator<SampleCommand>
{
    public SampleCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Nested).NotEmpty().WithMessage("Nested is required.");
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
        var result = Result<string>.Failure(Error.Validation("email", "Email is required."));

        result.ToActionResult().Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
