using FluentAssertions;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.Shared.Kernel.UnitTests.Results;

public sealed class ResultTests
{
    private static readonly Error AnyError = new("TEST_ERROR", "Something went wrong.");

    [Fact]
    public void Success_Should_HaveNoErrors_When_Created()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeNull();
        result.FirstError.Should().BeNull();
    }

    [Fact]
    public void Failure_Should_CarryTheError_When_CreatedWithOne()
    {
        var result = Result.Failure(AnyError);

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be(AnyError);
    }

    [Fact]
    public void Failure_Should_CarryEveryError_When_CreatedWithMany()
    {
        var errors = new[] { Error.Validation("email", "A."), Error.Validation("role", "B.") };

        var result = Result.Failure(errors);

        result.Errors.Should().HaveCount(2);
        result.FirstError!.Field.Should().Be("email");
    }

    [Fact]
    public void Failure_Should_Throw_When_ErrorListIsEmpty()
    {
        var act = () => Result.Failure([]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one error*");
    }

    [Fact]
    public void ImplicitOperator_Should_ProduceFailure_When_AssignedFromError()
    {
        Result result = AnyError;

        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be(AnyError);
    }
}

public sealed class ResultOfTTests
{
    private static readonly Error AnyError = new("TEST_ERROR", "Something went wrong.");

    [Fact]
    public void Success_Should_CarryTheValue_When_Created()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_Should_LeaveValueAtDefault_When_Created()
    {
        var result = Result<string>.Failure(AnyError);

        result.IsFailure.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void FromFailure_Should_PreserveEveryError_When_PropagatingAnotherResult()
    {
        // A secao 6 manda propagar, nao recriar: nenhum erro pode se perder no caminho.
        var original = Result.Failure([
            Error.Validation("email", "A."),
            Error.Validation("role", "B."),
        ]);

        var propagated = Result<string>.FromFailure(original);

        propagated.IsFailure.Should().BeTrue();
        propagated.Errors.Should().BeEquivalentTo(original.Errors);
    }

    [Fact]
    public void ImplicitOperator_Should_ProduceSuccess_When_AssignedFromValue()
    {
        Result<int> result = 7;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(7);
    }

    [Fact]
    public void ImplicitOperator_Should_ProduceFailure_When_AssignedFromError()
    {
        Result<int> result = AnyError;

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Map_Should_TransformValue_When_ResultIsSuccess()
    {
        var mapped = Result<int>.Success(21).Map(value => value * 2);

        mapped.Value.Should().Be(42);
    }

    [Fact]
    public void Map_Should_PropagateFailure_When_ResultIsFailure()
    {
        var mapped = Result<int>.Failure(AnyError).Map(value => value * 2);

        mapped.IsFailure.Should().BeTrue();
        mapped.FirstError.Should().Be(AnyError);
    }

    [Fact]
    public async Task BindAsync_Should_ChainOperation_When_ResultIsSuccess()
    {
        var bound = await Result<int>.Success(21)
            .BindAsync(value => Task.FromResult(Result<string>.Success($"valor {value}")));

        bound.Value.Should().Be("valor 21");
    }

    [Fact]
    public async Task BindAsync_Should_SkipOperation_When_ResultIsFailure()
    {
        var called = false;

        var bound = await Result<int>.Failure(AnyError)
            .BindAsync(_ =>
            {
                called = true;
                return Task.FromResult(Result<string>.Success("nao deveria rodar"));
            });

        called.Should().BeFalse();
        bound.IsFailure.Should().BeTrue();
    }
}
