using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.RemoveReviewReply;
using PetHost.Modules.Reviews.Application.Reviews.ReplyToReview;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Application.UnitTests.Reviews;

/// <summary>A resposta do avaliado: responder e apagar a resposta.</summary>
public sealed class ReplyHandlersTests
{
    private static readonly DateTimeOffset Now = ReviewBuilder.CheckedOutAt.AddDays(30);

    private readonly Mock<IReviewRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IReviewsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IReviewParties> _parties = ReviewsTestData.Parties();
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Review _review = new ReviewBuilder().Build();
    private readonly ReplyToReviewCommandHandler _reply;
    private readonly RemoveReviewReplyCommandHandler _remove;

    public ReplyHandlersTests()
    {
        var builder = new ReviewResponseBuilder(_parties.Object);
        var time = new FakeTimeProvider(Now);
        _reply = new ReplyToReviewCommandHandler(_repository.Object, _unitOfWork.Object, _parties.Object, builder, _auditTrail.Object, time);
        _remove = new RemoveReviewReplyCommandHandler(_repository.Object, _unitOfWork.Object, _parties.Object, builder, _auditTrail.Object, time);

        _repository.Setup(r => r.GetByIdAsync(_review.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_review);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private ReplyToReviewCommand HostReply(string text = "Obrigada, Camila!") =>
        new(ReviewBuilder.HostUserId, Roles.Host, _review.Id.Value, text);

    [Fact]
    public async Task Reply_Should_SaveAndRecord_When_TheHostAnswersTheOwnersReview()
    {
        // Act
        var result = await _reply.HandleAsync(HostReply(), CancellationToken.None);

        // Assert
        result.Value!.Reply.Should().Be(new ReviewReplyResponse("Obrigada, Camila!", Now));
        result.Value.BookingId.Should().BeNull();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewReplied && r.Details!["by"] == "host"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reply_Should_NotSave_When_TheReplyIsTheSame()
    {
        // Arrange
        _review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "Obrigada, Camila!", Now);

        // Act
        var result = await _reply.HandleAsync(HostReply(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reply_Should_ReturnNotFound_When_TheAuthorTriesToAnswerHerOwnReview()
    {
        // Act
        var result = await _reply.HandleAsync(new ReplyToReviewCommand(ReviewBuilder.OwnerUserId, Roles.Owner, _review.Id.Value, "Oi"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_review.Id));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reply_Should_ReturnNotFound_When_TheReviewDoesNotExist()
    {
        // Arrange
        _repository.Setup(r => r.GetByIdAsync(_review.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Review?)null);

        // Act
        var result = await _reply.HandleAsync(HostReply(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_review.Id));
    }

    [Fact]
    public async Task Reply_Should_ReturnHostDeactivated_When_TheHostIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ReviewBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewsTestData.UnavailableHost);

        // Act
        var result = await _reply.HandleAsync(HostReply(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.HostDeactivated);
    }

    [Fact]
    public async Task Reply_Should_PropagateTheSaveError_When_SavingFails()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.Conflict));

        // Act
        var result = await _reply.HandleAsync(HostReply(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remove_Should_ClearAndRecord_When_TheHostRemovesTheReply()
    {
        // Arrange
        _review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "Obrigada!", Now);

        // Act
        var result = await _remove.HandleAsync(new RemoveReviewReplyCommand(ReviewBuilder.HostUserId, Roles.Host, _review.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Reply.Should().BeNull();
        _auditTrail.Verify(
            a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewReplyRemoved), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Remove_Should_NotSave_When_ThereIsNoReply()
    {
        // Act
        var result = await _remove.HandleAsync(new RemoveReviewReplyCommand(ReviewBuilder.HostUserId, Roles.Host, _review.Id.Value), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remove_Should_ReturnNotFound_When_TheCallerIsNotTheReviewedParty()
    {
        // Act
        var result = await _remove.HandleAsync(new RemoveReviewReplyCommand(ReviewBuilder.OwnerUserId, Roles.Owner, _review.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_review.Id));
    }

    [Fact]
    public async Task Remove_Should_ReturnNotFound_When_TheReviewDoesNotExist()
    {
        // Arrange
        _repository.Setup(r => r.GetByIdAsync(_review.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Review?)null);

        // Act
        var result = await _remove.HandleAsync(new RemoveReviewReplyCommand(ReviewBuilder.HostUserId, Roles.Host, _review.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_review.Id));
    }

    [Fact]
    public async Task Remove_Should_ReturnHostNotFound_When_TheAccountHasNoHostProfile()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ReviewBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewerProfile?)null);

        // Act
        var result = await _remove.HandleAsync(new RemoveReviewReplyCommand(ReviewBuilder.HostUserId, Roles.Host, _review.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.HostNotFound);
    }

    [Fact]
    public async Task Remove_Should_PropagateTheSaveError_When_SavingFails()
    {
        // Arrange
        _review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "Obrigada!", Now);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.Conflict));

        // Act
        var result = await _remove.HandleAsync(new RemoveReviewReplyCommand(ReviewBuilder.HostUserId, Roles.Host, _review.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.Conflict);
    }

    [Fact]
    public async Task Handlers_Should_Throw_When_TheCommandIsNull()
    {
        // Act & Assert
        await ((Func<Task>)(() => _reply.HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => _remove.HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
    }
}
