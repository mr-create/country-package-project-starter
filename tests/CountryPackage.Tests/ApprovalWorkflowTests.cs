using System.Net;
using CountryPackage.Api.Contracts;
using CountryPackage.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using static CountryPackage.Tests.ApiTestHelpers;

namespace CountryPackage.Tests;

public sealed class ApprovalWorkflowTests
{
    [Fact]
    public async Task Full_http_flow_preserves_snapshots_and_completes_the_roadmap()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var create = await client.SendAsync(Command(HttpMethod.Post, "/api/packages", "editor-bgd", new
        {
            countryCode = "BGD",
            title = "Bangladesh Country Package 2027"
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var package = await create.BodyAsync<CountryPackageDto>();
        Assert.Equal(4, package.Steps.Count);
        Assert.Equal([StepKind.Decision, StepKind.Distribution, StepKind.Decision, StepKind.Distribution], package.Steps.Select(x => x.Kind));

        var source = Path.Combine(factory.RepositoryRoot, "sources", "BGD-country-context.docx");
        var upload = await UploadDocxAsync(client, "editor-bgd", package.Id, 1, source);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var versionOne = await upload.BodyAsync<DocumentVersionDto>();

        var submitCountry = await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/1/submit", "editor-bgd", new { reviewerUserId = "reviewer-bgd-country" }));
        Assert.Equal(HttpStatusCode.OK, submitCountry.StatusCode);

        var regionalCannotRead = await client.SendAsync(Read($"/api/reviewer/tasks/{package.Id}/steps/1", "reviewer-bgd-regional"));
        Assert.Equal(HttpStatusCode.NotFound, regionalCannotRead.StatusCode);

        var returnResponse = await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/1/review", "reviewer-bgd-country", new { decision = "Return", comment = "Clarify the delivery risks." }));
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);

        var resubmit = await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/1/submit", "editor-bgd", new { reviewerUserId = "reviewer-bgd-country" }));
        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);
        var approveCountry = await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/1/review", "reviewer-bgd-country", new { decision = "Approve" }));
        Assert.Equal(HttpStatusCode.OK, approveCountry.StatusCode);

        var distributeCountry = await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/2/submit", "editor-bgd", new { recipientUserId = "reviewer-bgd-country" }));
        Assert.Equal(HttpStatusCode.OK, distributeCountry.StatusCode);

        var afterCountry = await (await client.SendAsync(Read($"/api/packages/{package.Id}", "editor-bgd"))).BodyAsync<CountryPackageDto>();
        Assert.Equal(versionOne.Id, afterCountry.Steps[0].SnapshotDocumentVersionId);
        Assert.Equal(versionOne.Id, afterCountry.Steps[2].DraftDocumentVersionId);

        var submitRegional = await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/3/submit", "editor-bgd", new { reviewerUserId = "reviewer-bgd-regional" }));
        Assert.Equal(HttpStatusCode.OK, submitRegional.StatusCode);
        var approveRegional = await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/3/review", "reviewer-bgd-regional", new { decision = "Approve" }));
        Assert.Equal(HttpStatusCode.OK, approveRegional.StatusCode);
        var distributeRegional = await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/4/submit", "editor-bgd", new { recipientUserId = "reviewer-bgd-regional" }));
        Assert.Equal(HttpStatusCode.OK, distributeRegional.StatusCode);

        var completed = await (await client.SendAsync(Read($"/api/packages/{package.Id}", "editor-bgd"))).BodyAsync<CountryPackageDto>();
        Assert.Equal(PackageStatus.Completed, completed.Status);
        Assert.All(completed.Steps, step => Assert.Equal(StepStatus.Completed, step.Status));
        Assert.Equal(versionOne.Id, completed.Steps[0].SnapshotDocumentVersionId);
    }

    [Fact]
    public async Task Idempotent_create_replays_and_rejects_changed_payload()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        const string key = "same-create-command";

        var first = await client.SendAsync(Command(HttpMethod.Post, "/api/packages", "editor-bgd", new { countryCode = "BGD", title = "Package A" }, key));
        var second = await client.SendAsync(Command(HttpMethod.Post, "/api/packages", "editor-bgd", new { countryCode = "BGD", title = "Package A" }, key));
        var changed = await client.SendAsync(Command(HttpMethod.Post, "/api/packages", "editor-bgd", new { countryCode = "BGD", title = "Package B" }, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal((await first.BodyAsync<CountryPackageDto>()).Id, (await second.BodyAsync<CountryPackageDto>()).Id);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Fact]
    public async Task Country_scope_and_role_failures_are_enforced()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var wrongCountry = await client.SendAsync(Command(HttpMethod.Post, "/api/packages", "editor-bgd", new { countryCode = "KEN", title = "Out of scope" }));
        var wrongRole = await client.SendAsync(Command(HttpMethod.Post, "/api/packages", "reviewer-bgd-country", new { countryCode = "BGD", title = "Wrong role" }));

        Assert.Equal(HttpStatusCode.Forbidden, wrongCountry.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
    }

    [Fact]
    public async Task Concurrent_decisions_have_one_winner_and_one_audit_effect()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var package = await CreatePendingCountryDecisionAsync(factory, client);

        var approve = client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/1/review", "reviewer-bgd-country", new { decision = "Approve" }));
        var returned = client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/1/review", "reviewer-bgd-country", new { decision = "Return", comment = "Competing return" }));
        var responses = await Task.WhenAll(approve, returned);

        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        var auditResponse = await client.SendAsync(Read($"/api/packages/{package.Id}/steps/1/audit", "editor-bgd"));
        Assert.True(auditResponse.IsSuccessStatusCode, await auditResponse.Content.ReadAsStringAsync());
        var audit = await auditResponse.BodyAsync<IReadOnlyList<AuditEntryDto>>();
        Assert.Single(audit, x => x.Action is "DecisionApproved" or "DecisionReturned");
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_review_transition()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var package = await CreatePendingCountryDecisionAsync(factory, client);
        using (var scope = factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<AuditFailureSwitch>().Enabled = true;

        var failed = await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/1/review", "reviewer-bgd-country", new { decision = "Approve" }));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        using (var scope = factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<AuditFailureSwitch>().Enabled = false;
        var taskResponse = await client.SendAsync(Read("/api/reviewer/tasks", "reviewer-bgd-country"));
        Assert.True(taskResponse.IsSuccessStatusCode, await taskResponse.Content.ReadAsStringAsync());
        var tasks = await taskResponse.BodyAsync<IReadOnlyList<ReviewerTaskDto>>();
        Assert.Contains(tasks, x => x.PackageId == package.Id && x.StepOrder == 1);
    }

    private static async Task<CountryPackageDto> CreatePendingCountryDecisionAsync(TestApplicationFactory factory, HttpClient client)
    {
        var create = await client.SendAsync(Command(HttpMethod.Post, "/api/packages", "editor-bgd", new { countryCode = "BGD", title = "Concurrency package" }));
        var package = await create.BodyAsync<CountryPackageDto>();
        var source = Path.Combine(factory.RepositoryRoot, "sources", "BGD-country-context.docx");
        Assert.Equal(HttpStatusCode.Created, (await UploadDocxAsync(client, "editor-bgd", package.Id, 1, source)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Command(HttpMethod.Post, $"/api/packages/{package.Id}/steps/1/submit", "editor-bgd", new { reviewerUserId = "reviewer-bgd-country" }))).StatusCode);
        return package;
    }
}
