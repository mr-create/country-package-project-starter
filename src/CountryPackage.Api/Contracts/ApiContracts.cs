using CountryPackage.Api.Domain;

namespace CountryPackage.Api.Contracts;

public sealed record CreatePackageRequest(string CountryCode, string Title);
public sealed record SubmitStepRequest(string? ReviewerUserId, string? RecipientUserId);
public sealed record ReviewStepRequest(ReviewDecision Decision, string? Comment);

public sealed record PackageSummaryDto(
    Guid Id,
    string CountryCode,
    string Title,
    PackageStatus Status,
    int CurrentStepOrder,
    StepStatus CurrentStepStatus);

public sealed record CountryPackageDto(
    Guid Id,
    string CountryCode,
    string Title,
    PackageStatus Status,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ApprovalStepDto> Steps);

public sealed record ApprovalStepDto(
    Guid Id,
    int Order,
    StepKind Kind,
    Clearance? RequiredClearance,
    StepStatus Status,
    string? ReviewerUserId,
    string? RecipientUserId,
    Guid? DraftDocumentVersionId,
    Guid? SnapshotDocumentVersionId,
    Guid? DistributedDocumentVersionId,
    ReviewDecision? ReviewDecision,
    string? ReviewComment,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CompletedAt);

public sealed record DocumentVersionDto(
    Guid Id,
    string FileName,
    string ContentType,
    string Sha256,
    string UploadedBy,
    DateTimeOffset UploadedAt,
    EvidenceManifestDto? EvidenceManifest);

public sealed record EvidenceManifestInput(
    IReadOnlyList<string> SourceReferences,
    IReadOnlyList<string> Citations,
    IReadOnlyList<string> ValidationFindings,
    string WorkflowVersion,
    string ModelIdentifier,
    DateTimeOffset GeneratedAt);

public sealed record EvidenceManifestDto(
    IReadOnlyList<string> SourceReferences,
    IReadOnlyList<string> Citations,
    IReadOnlyList<string> ValidationFindings,
    string WorkflowVersion,
    string ModelIdentifier,
    DateTimeOffset GeneratedAt,
    string AcceptedBy);

public sealed record AuditEntryDto(
    Guid Id,
    int? StepOrder,
    string ActorUserId,
    string Action,
    object Details,
    DateTimeOffset OccurredAt,
    string TraceId);

public sealed record ReviewerTaskDto(
    Guid PackageId,
    string CountryCode,
    string Title,
    int StepOrder,
    Clearance RequiredClearance,
    Guid SnapshotDocumentVersionId,
    DateTimeOffset SubmittedAt);

public sealed record ReviewContextDto(
    Guid PackageId,
    string CountryCode,
    string Title,
    ApprovalStepDto Step,
    DocumentVersionDto Snapshot,
    IReadOnlyList<AuditEntryDto> Audit);

public sealed record DevelopmentPersonaDto(
    string UserId,
    string DisplayName,
    UserRole Role,
    IReadOnlyCollection<string> CountryScopes,
    Clearance? Clearance);

public sealed record CopilotPrepareRequest(
    string CountryCode,
    string? Instructions,
    string? ExistingDraft,
    string? ReviewComment);

public sealed record CopilotPrepareResponse(
    string Draft,
    IReadOnlyList<string> Citations,
    IReadOnlyList<string> Warnings,
    EvidenceManifestInput EvidenceManifest);

public sealed record CopilotExportRequest(string Title, string Draft);
