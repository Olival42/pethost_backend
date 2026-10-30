using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Hosts.IntegrationTests.HostApplications;

public sealed partial class HostApplicationFlowTests
{
    [Fact]
    public async Task DirectDocuments_Should_ConfirmAllSlotsTogetherAndRepeatWithoutReplacing_When_ImagesAreValid()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);
        var front = await StageDocumentAsync(host, "front", TestImages.PngVariant(21));
        var selfie = await StageDocumentAsync(host, "selfie", TestImages.PngVariant(22));
        Data(await SendAsync(HttpMethod.Get, ApplicationRoute, host)).GetProperty("application").GetProperty("documents").GetArrayLength().Should().Be(0);
        var body = new { uploadIds = new[] { front, selfie } };
        var first = await SendAsync(HttpMethod.Post, "/api/v1/uploads/documents/confirm", host, body);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(first).GetProperty("result").GetProperty("documents").GetArrayLength().Should().Be(2);
        var second = await SendAsync(HttpMethod.Post, "/api/v1/uploads/documents/confirm", host, body);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(second).GetProperty("result").GetRawText().Should().Be(Data(first).GetProperty("result").GetRawText());
    }

    [Fact]
    public async Task DirectDocuments_Should_RollBackEverySlot_When_OneImageIsInvalid()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);
        var front = await StageDocumentAsync(host, "front", TestImages.PngVariant(23));
        var back = await StageDocumentAsync(host, "back", "not an image"u8.ToArray());
        var refused = await SendAsync(HttpMethod.Post, "/api/v1/uploads/documents/confirm", host, new { uploadIds = new[] { front, back } });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Data(await SendAsync(HttpMethod.Get, ApplicationRoute, host)).GetProperty("application").GetProperty("documents").GetArrayLength().Should().Be(0);
        var retried = await SendAsync(HttpMethod.Post, "/api/v1/uploads/documents/confirm", host, new { uploadIds = new[] { front } });
        retried.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(retried).GetProperty("result").GetProperty("documents").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task DirectDocuments_Should_RefuseEmptyAndRepeatedSlots_When_TheBatchIsInvalid()
    {
        var host = await RegisterHostAccountAsync("cida@exemplo.com");
        await StartAsync(host);
        (await SendAsync(HttpMethod.Post, "/api/v1/uploads/documents/confirm", host, new { uploadIds = Array.Empty<Guid>() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var front = await StageDocumentAsync(host, "front", TestImages.PngVariant(24));
        var otherFront = await StageDocumentAsync(host, "front", TestImages.PngVariant(25));
        (await SendAsync(HttpMethod.Post, "/api/v1/uploads/documents/confirm", host, new { uploadIds = new[] { front, otherFront } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> StageDocumentAsync(string token, string kind, byte[] image)
    {
        var requested = await SendAsync(HttpMethod.Post, "/api/v1/uploads", token, new { purpose = "host-document", kind, contentType = "image/png", sizeBytes = image.Length });
        requested.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = Data(requested);
        using var browser = new HttpClient();
        using var content = new ByteArrayContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        (await browser.PutAsync(ticket.GetProperty("uploadUrl").GetString(), content, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        return ticket.GetProperty("uploadId").GetGuid();
    }
}
