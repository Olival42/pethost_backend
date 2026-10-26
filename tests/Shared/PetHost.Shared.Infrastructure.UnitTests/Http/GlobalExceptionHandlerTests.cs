using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PetHost.Shared.Infrastructure.Http;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Http;

/// <summary>
/// Exceção não tratada: 500 no envelope, só com o traceId, sem nada da exceção. Pedido inválido
/// barrado pelo servidor (corpo grande demais): o status dele, também no envelope.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    private const string Secret = "relation auth.users violates constraint pk_users";

    [Fact]
    public async Task TryHandleAsync_Should_Return500WithOnlyTheTraceId_When_AnExceptionEscapes()
    {
        // Arrange
        var context = new DefaultHttpContext { TraceIdentifier = "0HN7G4K2P9QRS:00000001" };
        context.Response.Body = new MemoryStream();
        Exception exception;
        try
        {
            throw new InvalidOperationException(Secret);
        }
        catch (InvalidOperationException thrown)
        {
            exception = thrown;
        }

        // Act
        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        text.Should().NotContain(Secret).And.NotContain(nameof(InvalidOperationException)).And.NotContain(" at ");

        var error = JsonDocument.Parse(text).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("UNEXPECTED_ERROR");
        error.GetProperty("message").GetString().Should().Be("An unexpected error occurred.");
        var details = error.GetProperty("details");
        details.EnumerateObject().Select(p => p.Name).Should().Equal("traceId");
        details.GetProperty("traceId").GetString().Should().Be("0HN7G4K2P9QRS:00000001");
    }

    [Theory]
    [InlineData(StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE")]
    [InlineData(StatusCodes.Status400BadRequest, "VALIDATION_ERROR")]
    public async Task TryHandleAsync_Should_ReturnTheRequestStatus_When_TheServerRejectedTheRequest(int status, string code)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var exception = new BadHttpRequestException("Request body too large. The max request body size is 30000000 bytes.", status);

        // Act
        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(status);

        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        text.Should().NotContain("30000000");

        var root = JsonDocument.Parse(text).RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
    }
}
