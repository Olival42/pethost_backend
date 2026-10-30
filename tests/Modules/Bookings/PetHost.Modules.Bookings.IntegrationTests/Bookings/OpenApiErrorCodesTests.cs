using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using PetHost.Shared.Kernel.Errors;
using Xunit;

namespace PetHost.Modules.Bookings.IntegrationTests.Bookings;

/// <summary>
/// O documento OpenAPI que o front usa para gerar os tipos: <c>ApiErrorCode</c> com todos os códigos
/// (gerado das classes <c>*Errors</c>) e <c>errors</c> alinhado a <c>messages</c> em cada campo de
/// <c>ApiFieldErrors</c>. Com a variável <c>PETHOST_OPENAPI_OUT</c>, grava o documento nesse caminho.
/// </summary>
[Collection(BookingsApiCollection.Name)]
public sealed class OpenApiErrorCodesTests(BookingsApiFixture fixture)
{
    private const string OutputVariable = "PETHOST_OPENAPI_OUT";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenApi_Should_ListEveryErrorCodeInApiErrorCode_AndAlignErrorsWithMessages()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(Ct);

        var schemas = JsonNode.Parse(text)!["components"]!["schemas"]!;

        var documented = schemas["ApiErrorCode"]!["enum"]!.AsArray().Select(code => code!.GetValue<string>()).ToList();
        documented.Should().OnlyHaveUniqueItems();
        documented.Should().BeEquivalentTo(ErrorCatalog.Codes(ErrorCatalog.DiscoverLoaded()), "the enum is generated from the *Errors classes, never listed by hand");
        documented.Should().Contain(["VALIDATION_ERROR", "BOOKING_CAPACITY_EXCEEDED", "BOOKING_CHECK_IN_TOO_SOON", "BOOKING_CARE_TERMS_NOT_ACCEPTED", "AUTH_EMAIL_REQUIRED"]);

        schemas["ApiFieldErrorItem"]!["required"]!.AsArray().Select(name => name!.GetValue<string>()).Should().Equal("code");
        schemas["ApiFieldErrorItem"]!["properties"]!["code"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/ApiErrorCode");
        
        schemas["ApiFieldErrors"]!["required"]!.AsArray().Select(name => name!.GetValue<string>()).Should().Contain("errors");
        schemas["ApiFieldErrors"]!["properties"]!["errors"]!["items"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/ApiFieldErrorItem");
        schemas["ApiError"]!["properties"]!["code"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/ApiErrorCode");

        // params no erro de topo (opcional) e nos itens de validação, ambos pelo mesmo schema.
        schemas["ApiError"]!["required"]!.AsArray().Select(name => name!.GetValue<string>()).Should().NotContain("params");
        schemas["ApiError"]!["properties"]!["params"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/ApiErrorParams");
        schemas["ApiFieldErrorItem"]!["properties"]!["params"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/ApiErrorParams");
        schemas["ApiErrorParams"]!["additionalProperties"]!["oneOf"]!.AsArray().Should().HaveCount(3);

        var output = Environment.GetEnvironmentVariable(OutputVariable);
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            var pretty = JsonSerializer.Serialize(JsonNode.Parse(text), Indented);
            await File.WriteAllTextAsync(output, pretty, Ct);
        }
    }
}
