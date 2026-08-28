using System.Text.Json;
using CountryPackage.Api.Auth;
using CountryPackage.Api.Contracts;
using CountryPackage.Api.Domain;
using CountryPackage.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CountryPackage.Api.Services;

public sealed class CountryPackageService(
    AppDbContext db,
    ICurrentUser currentUser,
    FictionalUserDirectory users,
    IdempotentExecutor idempotency,
    TimeProvider clock,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<IReadOnlyList<PackageSummaryDto>> ListPackagesAsync(CancellationToken cancellationToken)
    {
        var user = RequireRole(UserRole.CountryEditor);
        var scopes = user.CountryScopes.ToArray();
        var packages = await db.Packages.AsNoTracking().Include(x => x.Steps)
            .Where(x => scopes.Contains(x.CountryCode))
            .ToListAsync(cancellationToken);

        return packages.OrderByDescending(x => x.CreatedAt).Select(package =>
        {
            var current = package.Steps.OrderBy(x => x.Order).FirstOrDefault(x => x.Status != StepStatus.Completed)
                          ?? package.Steps.OrderByDescending(x => x.Order).First();
            return new PackageSummaryDto(package.Id, package.CountryCode, package.Title, package.Status, current.Order, current.Status);
        }).ToList();
    }

    public async Task<CountryPackageDto> GetPackageAsync(Guid packageId, CancellationToken cancellationToken)
    {
        var user = RequireRole(UserRole.CountryEditor);
        var package = await LoadPackageAsync(packageId, cancellationToken);
        EnsureEditorScope(user, package);
        return package.ToDto();
    }

    public Task<CommandResult<CountryPackageDto>> CreatePackageAsync(
        CreatePackageRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var user = RequireRole(UserRole.CountryEditor);
        var countryCode = request.CountryCode?.Trim().ToUpperInvariant() ?? "";
        var title = request.Title?.Trim() ?? "";

        if (countryCode.Length != 3 || countryCode.Any(x => x is < 'A' or > 'Z'))
            throw new ApiException(400, "package.invalid_country", "Country code must be a three-letter uppercase code.");
        if (title.Length is < 1 or > 200)
            throw new ApiException(400, "package.invalid_title", "Title must contain between 1 and 200 characters.");
        if (!user.CountryScopes.Contains(countryCode))
            throw new ApiException(403, "authorization.country_scope", "The Editor cannot create a package for this country.");

        return idempotency.ExecuteAsync(
            user.UserId, "package.create", key, IdempotentExecutor.Hash(countryCode, title), 201,
            () =>
            {
                var now = clock.GetUtcNow();
                var package = new CountryPackageEntity
                {
                    Id = Guid.NewGuid(),
                    CountryCode = countryCode,
                    Title = title,
                    CreatedBy = user.UserId,
                    CreatedAt = now,
                    Steps = CreateRoadmap()
                };
                db.Packages.Add(package);
                AddAudit(package.Id, null, user.UserId, "PackageCreated", new { countryCode, title });
                return Task.FromResult((package.ToDto(), package.Id));
            }, cancellationToken);
    }

    public async Task<CommandResult<DocumentVersionDto>> UploadDocumentAsync(
        Guid packageId,
        int order,
        ValidatedDocument upload,
        EvidenceManifestInput? manifest,
        string key,
        CancellationToken cancellationToken)
    {
        var user = RequireRole(UserRole.CountryEditor);
        var manifestHash = manifest is null ? null : JsonSerializer.Serialize(manifest, JsonDefaults.Options);

        return await idempotency.ExecuteAsync(
            user.UserId, $"step.{order}.document.upload", key,
            IdempotentExecutor.Hash(packageId.ToString(), order.ToString(), upload.Sha256, manifestHash), 201,
            async () =>
            {
                var package = await LoadPackageAsync(packageId, cancellationToken);
                EnsureEditorScope(user, package);
                var step = GetStep(package, order);
                EnsurePreviousStepsComplete(package, order);
                if (step.Kind != StepKind.Decision)
                    throw new ApiException(409, "workflow.document_not_applicable", "Documents can only be uploaded to decision steps.");
                if (step.Status is StepStatus.PendingReview or StepStatus.Completed)
                    throw new ApiException(409, "workflow.step_locked", "The decision document cannot be changed in the current state.");

                var document = new DocumentVersionEntity
                {
                    Id = Guid.NewGuid(),
                    CountryPackageId = package.Id,
                    FileName = upload.FileName,
                    ContentType = upload.ContentType,
                    Content = upload.Content,
                    Sha256 = upload.Sha256,
                    UploadedBy = user.UserId,
                    UploadedAt = clock.GetUtcNow()
                };

                if (manifest is not null)
                {
                    document.EvidenceManifest = new EvidenceManifestEntity
                    {
                        Id = Guid.NewGuid(),
                        DocumentVersionId = document.Id,
                        SourceReferencesJson = JsonSerializer.Serialize(manifest.SourceReferences, JsonDefaults.Options),
                        CitationsJson = JsonSerializer.Serialize(manifest.Citations, JsonDefaults.Options),
                        ValidationFindingsJson = JsonSerializer.Serialize(manifest.ValidationFindings, JsonDefaults.Options),
                        WorkflowVersion = manifest.WorkflowVersion,
                        ModelIdentifier = manifest.ModelIdentifier,
                        GeneratedAt = manifest.GeneratedAt,
                        AcceptedBy = user.UserId
                    };
                }

                db.Documents.Add(document);
                step.DraftDocumentVersionId = document.Id;
                step.Status = StepStatus.Draft;
                step.ConcurrencyVersion++;
                package.ConcurrencyVersion++;
                AddAudit(package.Id, order, user.UserId, "DocumentUploaded", new { documentVersionId = document.Id, upload.FileName, upload.Sha256, aiAssisted = manifest is not null });
                return (document.ToDto(), package.Id);
            }, cancellationToken);
    }

    public async Task<CommandResult<ApprovalStepDto>> SubmitStepAsync(
        Guid packageId,
        int order,
        SubmitStepRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var user = RequireRole(UserRole.CountryEditor);
        return await idempotency.ExecuteAsync(
            user.UserId, $"step.{order}.submit", key,
            IdempotentExecutor.Hash(packageId.ToString(), order.ToString(), request.ReviewerUserId, request.RecipientUserId), 200,
            async () =>
            {
                var package = await LoadPackageAsync(packageId, cancellationToken);
                EnsureEditorScope(user, package);
                EnsurePreviousStepsComplete(package, order);
                var step = GetStep(package, order);

                if (step.Kind == StepKind.Decision)
                    SubmitDecision(package, step, request);
                else
                    SubmitDistribution(package, step, request);

                package.ConcurrencyVersion++;
                return (step.ToDto(), package.Id);
            }, cancellationToken);
    }

    public async Task<CommandResult<ApprovalStepDto>> ReviewStepAsync(
        Guid packageId,
        int order,
        ReviewStepRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var user = RequireRole(UserRole.CountryReviewer);
        return await idempotency.ExecuteAsync(
            user.UserId, $"step.{order}.review", key,
            IdempotentExecutor.Hash(packageId.ToString(), order.ToString(), request.Decision.ToString(), request.Comment?.Trim()), 200,
            async () =>
            {
                var package = await LoadPackageAsync(packageId, cancellationToken);
                var step = GetStep(package, order);
                EnsureAssignedPendingReviewer(user, package, step);

                var comment = request.Comment?.Trim();
                if (request.Decision == ReviewDecision.Return && string.IsNullOrWhiteSpace(comment))
                    throw new ApiException(400, "review.comment_required", "A return comment is required.");
                if (comment?.Length > 2000)
                    throw new ApiException(400, "review.comment_too_long", "Review comments cannot exceed 2,000 characters.");

                step.ReviewDecision = request.Decision;
                step.ReviewComment = comment;
                if (request.Decision == ReviewDecision.Approve)
                {
                    step.Status = StepStatus.Completed;
                    step.CompletedAt = clock.GetUtcNow();
                }
                else
                {
                    step.Status = StepStatus.Returned;
                    step.CompletedAt = null;
                }

                step.ConcurrencyVersion++;
                package.ConcurrencyVersion++;
                AddAudit(package.Id, order, user.UserId, request.Decision == ReviewDecision.Approve ? "DecisionApproved" : "DecisionReturned",
                    new { snapshotDocumentVersionId = step.SnapshotDocumentVersionId, comment });
                return (step.ToDto(), package.Id);
            }, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewerTaskDto>> GetReviewerTasksAsync(CancellationToken cancellationToken)
    {
        var user = RequireRole(UserRole.CountryReviewer);
        var scopes = user.CountryScopes.ToArray();
        var tasks = await db.Steps.AsNoTracking()
            .Where(step => step.ReviewerUserId == user.UserId && step.Status == StepStatus.PendingReview &&
                           step.RequiredClearance == user.Clearance && scopes.Contains(step.CountryPackage.CountryCode))
            .Select(step => new ReviewerTaskDto(
                step.CountryPackageId,
                step.CountryPackage.CountryCode,
                step.CountryPackage.Title,
                step.Order,
                step.RequiredClearance!.Value,
                step.SnapshotDocumentVersionId!.Value,
                step.SubmittedAt!.Value))
            .ToListAsync(cancellationToken);
        return tasks.OrderBy(x => x.SubmittedAt).ToList();
    }

    public async Task<ReviewContextDto> GetReviewContextAsync(Guid packageId, int order, CancellationToken cancellationToken)
    {
        var user = RequireRole(UserRole.CountryReviewer);
        var package = await LoadPackageAsync(packageId, cancellationToken);
        var step = GetStep(package, order);
        EnsureAssignedPendingReviewer(user, package, step);
        var snapshot = await db.Documents.AsNoTracking().Include(x => x.EvidenceManifest)
            .SingleAsync(x => x.Id == step.SnapshotDocumentVersionId, cancellationToken);
        var audit = await LoadAuditAsync(packageId, order, cancellationToken);
        return new(package.Id, package.CountryCode, package.Title, step.ToDto(), snapshot.ToDto(), audit);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetStepAuditAsync(Guid packageId, int order, CancellationToken cancellationToken)
    {
        var package = await LoadPackageAsync(packageId, cancellationToken);
        var step = GetStep(package, order);
        var user = RequireUser();
        if (user.Role == UserRole.CountryEditor)
            EnsureEditorScope(user, package);
        else
            EnsureAssignedPendingReviewer(user, package, step);
        return await LoadAuditAsync(packageId, order, cancellationToken);
    }

    public async Task<DocumentVersionEntity> GetStepDocumentAsync(Guid packageId, int order, CancellationToken cancellationToken)
    {
        var package = await LoadPackageAsync(packageId, cancellationToken);
        var step = GetStep(package, order);
        var user = RequireUser();
        Guid? documentId;
        if (user.Role == UserRole.CountryEditor)
        {
            EnsureEditorScope(user, package);
            documentId = step.DistributedDocumentVersionId ?? step.SnapshotDocumentVersionId ?? step.DraftDocumentVersionId;
        }
        else
        {
            EnsureAssignedPendingReviewer(user, package, step);
            documentId = step.SnapshotDocumentVersionId;
        }

        if (documentId is null)
            throw NotFound();
        return await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken) ?? throw NotFound();
    }

    private void SubmitDecision(CountryPackageEntity package, ApprovalStepEntity step, SubmitStepRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewerUserId) || request.RecipientUserId is not null)
            throw new ApiException(400, "step.reviewer_required", "Decision steps require exactly one reviewerUserId.");
        if (step.Status is not (StepStatus.Draft or StepStatus.Returned) || step.DraftDocumentVersionId is null)
            throw new ApiException(409, "workflow.document_required", "The decision step requires a draft document before submission.");

        var reviewer = users.Find(request.ReviewerUserId) ?? throw new ApiException(400, "step.invalid_reviewer", "The selected reviewer does not exist.");
        if (reviewer.Role != UserRole.CountryReviewer || reviewer.Clearance != step.RequiredClearance || !reviewer.CountryScopes.Contains(package.CountryCode))
            throw new ApiException(400, "step.invalid_reviewer", "The selected reviewer does not have the required country scope and clearance.");

        step.ReviewerUserId = reviewer.UserId;
        step.RecipientUserId = null;
        step.SnapshotDocumentVersionId = step.DraftDocumentVersionId;
        step.Status = StepStatus.PendingReview;
        step.ReviewDecision = null;
        step.ReviewComment = null;
        step.SubmittedAt = clock.GetUtcNow();
        step.CompletedAt = null;
        step.ConcurrencyVersion++;
        AddAudit(package.Id, step.Order, RequireUser().UserId, "DecisionSubmitted",
            new { reviewerUserId = reviewer.UserId, snapshotDocumentVersionId = step.SnapshotDocumentVersionId, requiredClearance = step.RequiredClearance });
    }

    private void SubmitDistribution(CountryPackageEntity package, ApprovalStepEntity step, SubmitStepRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RecipientUserId) || request.ReviewerUserId is not null)
            throw new ApiException(400, "step.recipient_required", "Distribution steps require exactly one recipientUserId.");
        if (step.Status == StepStatus.Completed)
            throw new ApiException(409, "workflow.step_completed", "The distribution step is already complete.");

        var recipient = users.Find(request.RecipientUserId) ?? throw new ApiException(400, "step.invalid_recipient", "The selected recipient does not exist.");
        if (recipient.Role != UserRole.CountryReviewer || recipient.Clearance != step.RequiredClearance || !recipient.CountryScopes.Contains(package.CountryCode))
            throw new ApiException(400, "step.invalid_recipient", "The selected recipient does not have the required country scope and organizational level.");

        var precedingDecision = GetStep(package, step.Order - 1);
        if (precedingDecision.Status != StepStatus.Completed || precedingDecision.SnapshotDocumentVersionId is null)
            throw new ApiException(409, "workflow.approved_snapshot_required", "Distribution requires the preceding approved decision snapshot.");

        step.RecipientUserId = recipient.UserId;
        step.DistributedDocumentVersionId = precedingDecision.SnapshotDocumentVersionId;
        step.Status = StepStatus.Completed;
        step.SubmittedAt = clock.GetUtcNow();
        step.CompletedAt = step.SubmittedAt;
        step.ConcurrencyVersion++;

        if (step.Order == 2)
        {
            var regionalDecision = GetStep(package, 3);
            regionalDecision.DraftDocumentVersionId = precedingDecision.SnapshotDocumentVersionId;
            regionalDecision.Status = StepStatus.Draft;
            regionalDecision.ConcurrencyVersion++;
            AddAudit(package.Id, 3, RequireUser().UserId, "DraftInitializedFromApprovedSnapshot",
                new { documentVersionId = precedingDecision.SnapshotDocumentVersionId, sourceStepOrder = 1 });
        }
        else if (step.Order == 4)
        {
            package.Status = PackageStatus.Completed;
        }

        AddAudit(package.Id, step.Order, RequireUser().UserId, "PackageDistributed",
            new { recipientUserId = recipient.UserId, documentVersionId = step.DistributedDocumentVersionId, level = step.RequiredClearance });
    }

    private async Task<CountryPackageEntity> LoadPackageAsync(Guid packageId, CancellationToken cancellationToken) =>
        await db.Packages.Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == packageId, cancellationToken) ?? throw NotFound();

    private async Task<IReadOnlyList<AuditEntryDto>> LoadAuditAsync(Guid packageId, int order, CancellationToken cancellationToken) =>
        (await db.AuditEntries.AsNoTracking()
            .Where(x => x.CountryPackageId == packageId && (x.StepOrder == null || x.StepOrder == order))
            .ToListAsync(cancellationToken)).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Select(x => x.ToDto()).ToList();

    private static List<ApprovalStepEntity> CreateRoadmap() =>
    [
        Step(1, StepKind.Decision, Clearance.Country),
        Step(2, StepKind.Distribution, Clearance.Country),
        Step(3, StepKind.Decision, Clearance.Regional),
        Step(4, StepKind.Distribution, Clearance.Regional)
    ];

    private static ApprovalStepEntity Step(int order, StepKind kind, Clearance clearance) => new()
    {
        Id = Guid.NewGuid(),
        Order = order,
        Kind = kind,
        RequiredClearance = clearance
    };

    private static ApprovalStepEntity GetStep(CountryPackageEntity package, int order) =>
        package.Steps.SingleOrDefault(x => x.Order == order) ?? throw NotFound();

    private static void EnsurePreviousStepsComplete(CountryPackageEntity package, int order)
    {
        if (package.Steps.Any(x => x.Order < order && x.Status != StepStatus.Completed))
            throw new ApiException(409, "workflow.previous_step_incomplete", "All preceding roadmap steps must be complete.");
    }

    private static void EnsureEditorScope(FictionalUser user, CountryPackageEntity package)
    {
        if (user.Role != UserRole.CountryEditor || !user.CountryScopes.Contains(package.CountryCode))
            throw NotFound();
    }

    private static void EnsureAssignedPendingReviewer(FictionalUser user, CountryPackageEntity package, ApprovalStepEntity step)
    {
        if (user.Role != UserRole.CountryReviewer || !user.CountryScopes.Contains(package.CountryCode) ||
            user.Clearance != step.RequiredClearance || step.ReviewerUserId != user.UserId || step.Status != StepStatus.PendingReview)
            throw NotFound();
    }

    private FictionalUser RequireUser() => currentUser.User ?? throw new ApiException(401, "identity.required", "A valid development identity is required.");

    private FictionalUser RequireRole(UserRole role)
    {
        var user = RequireUser();
        if (user.Role != role)
            throw new ApiException(403, "authorization.role", $"This operation requires the {role} role.");
        return user;
    }

    private void AddAudit(Guid packageId, int? stepOrder, string actor, string action, object details)
    {
        db.AuditEntries.Add(new AuditEntryEntity
        {
            Id = Guid.NewGuid(),
            CountryPackageId = packageId,
            StepOrder = stepOrder,
            ActorUserId = actor,
            Action = action,
            SafeDetailsJson = JsonSerializer.Serialize(details, JsonDefaults.Options),
            OccurredAt = clock.GetUtcNow(),
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N")
        });
    }

    private static ApiException NotFound() => new(404, "resource.not_found", "The requested resource was not found.");
}
