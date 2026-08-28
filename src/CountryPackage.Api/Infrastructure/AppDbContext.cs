using CountryPackage.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CountryPackage.Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CountryPackageEntity> Packages => Set<CountryPackageEntity>();
    public DbSet<ApprovalStepEntity> Steps => Set<ApprovalStepEntity>();
    public DbSet<DocumentVersionEntity> Documents => Set<DocumentVersionEntity>();
    public DbSet<EvidenceManifestEntity> EvidenceManifests => Set<EvidenceManifestEntity>();
    public DbSet<AuditEntryEntity> AuditEntries => Set<AuditEntryEntity>();
    public DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CountryPackageEntity>(entity =>
        {
            entity.ToTable("CountryPackages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CountryCode).HasMaxLength(3);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            entity.HasIndex(x => x.CountryCode);
            entity.HasMany(x => x.Steps).WithOne(x => x.CountryPackage).HasForeignKey(x => x.CountryPackageId);
        });

        modelBuilder.Entity<ApprovalStepEntity>(entity =>
        {
            entity.ToTable("ApprovalSteps", table => table.HasCheckConstraint("CK_ApprovalSteps_Order", "\"Order\" BETWEEN 1 AND 4"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasConversion<string>();
            entity.Property(x => x.RequiredClearance).HasConversion<string>();
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.ReviewDecision).HasConversion<string>();
            entity.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            entity.HasIndex(x => new { x.CountryPackageId, x.Order }).IsUnique();
            entity.HasIndex(x => new { x.ReviewerUserId, x.Status });
            entity.HasOne<DocumentVersionEntity>().WithMany().HasForeignKey(x => x.DraftDocumentVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentVersionEntity>().WithMany().HasForeignKey(x => x.SnapshotDocumentVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentVersionEntity>().WithMany().HasForeignKey(x => x.DistributedDocumentVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentVersionEntity>(entity =>
        {
            entity.ToTable("DocumentVersions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Content).IsRequired();
            entity.HasIndex(x => new { x.CountryPackageId, x.UploadedAt });
            entity.HasOne<CountryPackageEntity>().WithMany().HasForeignKey(x => x.CountryPackageId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.EvidenceManifest).WithOne(x => x.DocumentVersion)
                .HasForeignKey<EvidenceManifestEntity>(x => x.DocumentVersionId);
        });

        modelBuilder.Entity<EvidenceManifestEntity>().ToTable("EvidenceManifests");

        modelBuilder.Entity<AuditEntryEntity>(entity =>
        {
            entity.ToTable("AuditEntries");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CountryPackageId, x.StepOrder, x.OccurredAt });
            entity.HasOne<CountryPackageEntity>().WithMany().HasForeignKey(x => x.CountryPackageId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdempotencyRecordEntity>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ActorUserId, x.Operation, x.Key }).IsUnique();
            entity.HasOne<CountryPackageEntity>().WithMany().HasForeignKey(x => x.CountryPackageId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
