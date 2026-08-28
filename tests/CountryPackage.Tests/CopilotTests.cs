using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using CountryPackage.Api.Contracts;
using static CountryPackage.Tests.ApiTestHelpers;

namespace CountryPackage.Tests;

public sealed class CopilotTests
{
    [Fact]
    public async Task Authorized_editor_can_prepare_and_export_a_grounded_docx()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var prepare = Command(HttpMethod.Post, "/api/copilot/prepare", "editor-bgd", new
        {
            countryCode = "BGD",
            instructions = "Focus on delivery risks and mitigations."
        });
        prepare.Headers.Remove("Idempotency-Key");

        var response = await client.SendAsync(prepare);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.BodyAsync<CopilotPrepareResponse>();
        Assert.NotEmpty(result.Citations);
        Assert.All(result.EvidenceManifest.SourceReferences, source => Assert.StartsWith("BGD-", source));

        var export = new HttpRequestMessage(HttpMethod.Post, "/api/copilot/export")
        {
            Content = JsonContent.Create(new { title = "BGD package", draft = result.Draft }, options: Json)
        };
        export.Headers.Add("X-User-Id", "editor-bgd");
        var document = await client.SendAsync(export);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        using var archive = new ZipArchive(await document.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, entry => entry.FullName == "word/document.xml");
    }

    [Fact]
    public async Task Reviewer_cannot_use_editor_copilot()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var request = Command(HttpMethod.Post, "/api/copilot/prepare", "reviewer-bgd-country", new { countryCode = "BGD" });
        request.Headers.Remove("Idempotency-Key");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }
}
