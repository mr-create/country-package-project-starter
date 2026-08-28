namespace CountryPackage.Api.Domain;

public enum UserRole { CountryEditor, CountryReviewer }
public enum Clearance { Country, Regional }
public enum PackageStatus { InProgress, Completed }
public enum StepKind { Decision, Distribution }
public enum StepStatus { NotStarted, Draft, PendingReview, Returned, Completed }
public enum ReviewDecision { Approve, Return }

public sealed class CountryPackageEntity
{
    public Guid Id { get; set; }
    public required string CountryCode { get; set; }
    public required string Title { get; set; }
    public PackageStatus Status { get; set; } = PackageStatus.InProgress;
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int ConcurrencyVersion { get; set; }
    public List<ApprovalStepEntity> Steps { get; set; } = [];
}

public sealed class ApprovalStepEntity
{
    public Guid Id { get; set; }
    public Guid CountryPackageId { get; set; }
    public CountryPackageEntity CountryPackage { get; set; } = null!;
    public int Order { get; set; }
    public StepKind Kind { get; set; }
    public Clearance? RequiredClearance { get; set; }
    public StepStatus Status { get; set; } = StepStatus.NotStarted;
    public string? ReviewerUserId { get; set; }
    public string? RecipientUserId { get; set; }
    public Guid? DraftDocumentVersionId { get; set; }
    public Guid? SnapshotDocumentVersionId { get; set; }
    public Guid? DistributedDocumentVersionId { get; set; }
    public ReviewDecision? ReviewDecision { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int ConcurrencyVersion { get; set; }
}

public sealed class DocumentVersionEntity
{
    public Guid Id { get; set; }
    public Guid CountryPackageId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required byte[] Content { get; set; }
    public required string Sha256 { get; set; }
    public required string UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public EvidenceManifestEntity? EvidenceManifest { get; set; }
}

public sealed class EvidenceManifestEntity
{
    public Guid Id { get; set; }
    public Guid DocumentVersionId { get; set; }
    public DocumentVersionEntity DocumentVersion { get; set; } = null!;
    public required string SourceReferencesJson { get; set; }
    public required string CitationsJson { get; set; }
    public required string ValidationFindingsJson { get; set; }
    public required string WorkflowVersion { get; set; }
    public required string ModelIdentifier { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public required string AcceptedBy { get; set; }
}

public sealed class AuditEntryEntity
{
    public Guid Id { get; set; }
    public Guid CountryPackageId { get; set; }
    public int? StepOrder { get; set; }
    public required string ActorUserId { get; set; }
    public required string Action { get; set; }
    public required string SafeDetailsJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string TraceId { get; set; }
}

public sealed class IdempotencyRecordEntity
{
    public Guid Id { get; set; }
    public Guid CountryPackageId { get; set; }
    public required string ActorUserId { get; set; }
    public required string Operation { get; set; }
    public required string Key { get; set; }
    public required string RequestHash { get; set; }
    public required string ResponseJson { get; set; }
    public int StatusCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
