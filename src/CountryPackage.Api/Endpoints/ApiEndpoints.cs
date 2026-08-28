using System.Text.Json;
using CountryPackage.Api.Auth;
using CountryPackage.Api.Contracts;
using CountryPackage.Api.Infrastructure;
using CountryPackage.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CountryPackage.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapCountryPackageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var packages = endpoints.MapGroup("/api/packages");
        packages.MapGet("/", async (CountryPackageService service, CancellationToken ct) =>
            Results.Ok(await service.ListPackagesAsync(ct)));
        packages.MapPost("/", async (CreatePackageRequest request, HttpRequest http, CountryPackageService service, CancellationToken ct) =>
            Command(await service.CreatePackageAsync(request, IdempotencyKey(http), ct)));
        packages.MapGet("/{packageId:guid}", async (Guid packageId, CountryPackageService service, CancellationToken ct) =>
            Results.Ok(await service.GetPackageAsync(packageId, ct)));

        packages.MapPost("/{packageId:guid}/steps/{order:int}/document", UploadDocumentAsync)
            .DisableAntiforgery();
        packages.MapGet("/{packageId:guid}/steps/{order:int}/document", async (
            Guid packageId, int order, CountryPackageService service, CancellationToken ct) =>
        {
            var document = await service.GetStepDocumentAsync(packageId, order, ct);
            return Results.File(document.Content, document.ContentType, document.FileName, enableRangeProcessing: true,
                entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{document.Sha256}\""));
        });
        packages.MapPost("/{packageId:guid}/steps/{order:int}/submit", async (
            Guid packageId, int order, SubmitStepRequest request, HttpRequest http, CountryPackageService service, CancellationToken ct) =>
            Command(await service.SubmitStepAsync(packageId, order, request, IdempotencyKey(http), ct)));
        packages.MapPost("/{packageId:guid}/steps/{order:int}/review", async (
            Guid packageId, int order, ReviewStepRequest request, HttpRequest http, CountryPackageService service, CancellationToken ct) =>
            Command(await service.ReviewStepAsync(packageId, order, request, IdempotencyKey(http), ct)));
        packages.MapGet("/{packageId:guid}/steps/{order:int}/audit", async (
            Guid packageId, int order, CountryPackageService service, CancellationToken ct) =>
            Results.Ok(await service.GetStepAuditAsync(packageId, order, ct)));

        var reviews = endpoints.MapGroup("/api/reviewer/tasks");
        reviews.MapGet("/", async (CountryPackageService service, CancellationToken ct) =>
            Results.Ok(await service.GetReviewerTasksAsync(ct)));
        reviews.MapGet("/{packageId:guid}/steps/{order:int}", async (
            Guid packageId, int order, CountryPackageService service, CancellationToken ct) =>
            Results.Ok(await service.GetReviewContextAsync(packageId, order, ct)));

        endpoints.MapGet("/api/dev/personas", (FictionalUserDirectory directory, IWebHostEnvironment environment) =>
            environment.IsDevelopment() || environment.IsEnvironment("Testing")
                ? Results.Ok(directory.All.Select(x => new DevelopmentPersonaDto(x.UserId, x.DisplayName, x.Role, x.CountryScopes.ToArray(), x.Clearance)))
                : Results.NotFound());

        endpoints.MapPost("/api/copilot/prepare", async (CopilotPrepareRequest request, EvidenceCopilotService service, CancellationToken ct) =>
            Results.Ok(await service.PrepareAsync(request, ct)));
        endpoints.MapPost("/api/copilot/export", (CopilotExportRequest request, EvidenceCopilotService service) =>
        {
            var bytes = service.Export(request);
            var safeTitle = string.Join('-', request.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{safeTitle}.docx");
        });

        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
        endpoints.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct)
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "unavailable" }, statusCode: 503));
        return endpoints;
    }

    private static async Task<IResult> UploadDocumentAsync(
        Guid packageId,
        int order,
        HttpRequest request,
        DocumentValidator validator,
        CountryPackageService service,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            throw new ApiException(415, "document.multipart_required", "Document upload requires multipart/form-data.");
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? throw new ApiException(400, "document.file_required", "A file field is required.");
        var upload = await validator.ValidateAsync(file, cancellationToken);
        EvidenceManifestInput? manifest = null;
        var manifestJson = form["evidenceManifest"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(manifestJson))
        {
            try
            {
                manifest = JsonSerializer.Deserialize<EvidenceManifestInput>(manifestJson, JsonDefaults.Options);
            }
            catch (JsonException)
            {
                throw new ApiException(400, "document.invalid_manifest", "The evidence manifest is not valid JSON.");
            }
        }

        return Command(await service.UploadDocumentAsync(packageId, order, upload, manifest, IdempotencyKey(request), cancellationToken));
    }

    private static string IdempotencyKey(HttpRequest request) => request.Headers["Idempotency-Key"].FirstOrDefault() ?? "";

    private static IResult Command<T>(CommandResult<T> result) =>
        Results.Json(result.Value, JsonDefaults.Options, statusCode: result.StatusCode);
}
