using System.Text.Json;
using CountryPackage.Api.Contracts;
using CountryPackage.Api.Domain;

namespace CountryPackage.Api.Services;

public static class ContractMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CountryPackageDto ToDto(this CountryPackageEntity package) => new(
        package.Id, package.CountryCode, package.Title, package.Status, package.CreatedBy, package.CreatedAt,
        package.Steps.OrderBy(x => x.Order).Select(x => x.ToDto()).ToList());

    public static ApprovalStepDto ToDto(this ApprovalStepEntity step) => new(
        step.Id, step.Order, step.Kind, step.RequiredClearance, step.Status,
        step.ReviewerUserId, step.RecipientUserId, step.DraftDocumentVersionId,
        step.SnapshotDocumentVersionId, step.DistributedDocumentVersionId,
        step.ReviewDecision, step.ReviewComment, step.SubmittedAt, step.CompletedAt);

    public static DocumentVersionDto ToDto(this DocumentVersionEntity document) => new(
        document.Id, document.FileName, document.ContentType, document.Sha256, document.UploadedBy,
        document.UploadedAt, document.EvidenceManifest?.ToDto());

    public static EvidenceManifestDto ToDto(this EvidenceManifestEntity manifest) => new(
        DeserializeList(manifest.SourceReferencesJson), DeserializeList(manifest.CitationsJson),
        DeserializeList(manifest.ValidationFindingsJson), manifest.WorkflowVersion,
        manifest.ModelIdentifier, manifest.GeneratedAt, manifest.AcceptedBy);

    public static AuditEntryDto ToDto(this AuditEntryEntity entry) => new(
        entry.Id, entry.StepOrder, entry.ActorUserId, entry.Action,
        JsonSerializer.Deserialize<object>(entry.SafeDetailsJson, Json) ?? new { },
        entry.OccurredAt, entry.TraceId);

    private static IReadOnlyList<string> DeserializeList(string value) =>
        JsonSerializer.Deserialize<List<string>>(value, Json) ?? [];
}
