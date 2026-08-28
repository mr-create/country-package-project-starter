using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CountryPackage.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CountryPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CountryPackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepOrder = table.Column<int>(type: "INTEGER", nullable: true),
                    ActorUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    SafeDetailsJson = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEntries_CountryPackages_CountryPackageId",
                        column: x => x.CountryPackageId,
                        principalTable: "CountryPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CountryPackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_CountryPackages_CountryPackageId",
                        column: x => x.CountryPackageId,
                        principalTable: "CountryPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CountryPackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Operation = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_CountryPackages_CountryPackageId",
                        column: x => x.CountryPackageId,
                        principalTable: "CountryPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CountryPackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredClearance = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewerUserId = table.Column<string>(type: "TEXT", nullable: true),
                    RecipientUserId = table.Column<string>(type: "TEXT", nullable: true),
                    DraftDocumentVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SnapshotDocumentVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DistributedDocumentVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewDecision = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewComment = table.Column<string>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalSteps", x => x.Id);
                    table.CheckConstraint("CK_ApprovalSteps_Order", "\"Order\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_CountryPackages_CountryPackageId",
                        column: x => x.CountryPackageId,
                        principalTable: "CountryPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_DocumentVersions_DistributedDocumentVersionId",
                        column: x => x.DistributedDocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_DocumentVersions_DraftDocumentVersionId",
                        column: x => x.DraftDocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_DocumentVersions_SnapshotDocumentVersionId",
                        column: x => x.SnapshotDocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceManifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceReferencesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CitationsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ValidationFindingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ModelIdentifier = table.Column<string>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AcceptedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceManifests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceManifests_DocumentVersions_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_CountryPackageId_Order",
                table: "ApprovalSteps",
                columns: new[] { "CountryPackageId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_DistributedDocumentVersionId",
                table: "ApprovalSteps",
                column: "DistributedDocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_DraftDocumentVersionId",
                table: "ApprovalSteps",
                column: "DraftDocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_ReviewerUserId_Status",
                table: "ApprovalSteps",
                columns: new[] { "ReviewerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_SnapshotDocumentVersionId",
                table: "ApprovalSteps",
                column: "SnapshotDocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_CountryPackageId_StepOrder_OccurredAt",
                table: "AuditEntries",
                columns: new[] { "CountryPackageId", "StepOrder", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CountryPackages_CountryCode",
                table: "CountryPackages",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_CountryPackageId_UploadedAt",
                table: "DocumentVersions",
                columns: new[] { "CountryPackageId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceManifests_DocumentVersionId",
                table: "EvidenceManifests",
                column: "DocumentVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ActorUserId_Operation_Key",
                table: "IdempotencyRecords",
                columns: new[] { "ActorUserId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_CountryPackageId",
                table: "IdempotencyRecords",
                column: "CountryPackageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalSteps");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "EvidenceManifests");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "DocumentVersions");

            migrationBuilder.DropTable(
                name: "CountryPackages");
        }
    }
}
