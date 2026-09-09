using AI.DocumentReader.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Infrastructure;

public class DocumentDbContext : DbContext
{
    public DocumentDbContext(DbContextOptions<DocumentDbContext> options) : base(options)
    {
    }

    public DbSet<MedicalReport> MedicalReports => Set<MedicalReport>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<ResultCorrectionAudit> ResultCorrectionAudits => Set<ResultCorrectionAudit>();
    public DbSet<ValidationIssue> ValidationIssues => Set<ValidationIssue>();
    public DbSet<ExplanationRecord> ExplanationRecords => Set<ExplanationRecord>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();
    public DbSet<PatientAccessGrant> PatientAccessGrants => Set<PatientAccessGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>().HasKey(x => x.Id);
        modelBuilder.Entity<ApplicationUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<PatientProfile>().HasKey(x => x.Id);
        modelBuilder.Entity<Organization>().HasKey(x => x.Id);
        modelBuilder.Entity<SecurityAuditEvent>().HasKey(x => x.Id);
        modelBuilder.Entity<PatientAccessGrant>().HasKey(x => x.Id);
        modelBuilder.Entity<PatientAccessGrant>().HasIndex(x => new { x.PatientId, x.GrantedToUserId, x.GrantedToOrganizationId });

        // MedicalReport
        modelBuilder.Entity<MedicalReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.StoredFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.HasMany(e => e.LabResults)
                  .WithOne(e => e.MedicalReport)
                  .HasForeignKey(e => e.MedicalReportId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.AnalysisRuns)
                  .WithOne(e => e.MedicalReport)
                  .HasForeignKey(e => e.MedicalReportId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UploadedAt);
            entity.HasIndex(e => e.Status);
        });

        // LabResult
        modelBuilder.Entity<LabResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalTestName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.NormalizedTestName).HasMaxLength(255);
            entity.Property(e => e.ValueText).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Unit).HasMaxLength(50);
            entity.Property(e => e.OriginalUnit).HasMaxLength(50);
            entity.Property(e => e.NormalizedUnit).HasMaxLength(50);
            entity.Property(e => e.ValueOperator).HasMaxLength(5);
            entity.Property(e => e.ReferenceText).HasMaxLength(100);
            entity.Property(e => e.ReferenceType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.ReferenceOperator).HasMaxLength(5);
            entity.Property(e => e.ReportedFlag).HasMaxLength(20);
            entity.Property(e => e.SectionName).HasMaxLength(100);
            entity.Property(e => e.MethodText).HasMaxLength(255);
            entity.Property(e => e.AmbiguityReason).HasMaxLength(500);
            entity.Property(e => e.FlagDiscrepancy).HasMaxLength(500);
            entity.Property(e => e.CalculatedStatus)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(e => e.ValueNumeric).HasPrecision(18, 4);
            entity.Property(e => e.ReferenceMin).HasPrecision(18, 4);
            entity.Property(e => e.ReferenceMax).HasPrecision(18, 4);
            entity.Property(e => e.ExtractionConfidence).HasPrecision(5, 4);

            entity.HasIndex(e => e.MedicalReportId);
            entity.HasIndex(e => e.CalculatedStatus);
        });

        modelBuilder.Entity<ExplanationRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Mode).IsRequired().HasMaxLength(40);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Model).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PromptVersion).IsRequired().HasMaxLength(50);
            entity.Property(e => e.GeneratedText).IsRequired();
            entity.Property(e => e.GeneratedJson).IsRequired();
            entity.HasIndex(e => e.MedicalReportId);
        });

        modelBuilder.Entity<ValidationIssue>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Severity).IsRequired().HasMaxLength(20);
            entity.Property(e => e.FieldName).HasMaxLength(100);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.ResolutionType).HasMaxLength(50);
            entity.HasOne(e => e.LabResult).WithMany(e => e.ValidationIssues).HasForeignKey(e => e.LabResultId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.LabResultId);
        });

        modelBuilder.Entity<ResultCorrectionAudit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FieldName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PreviousValue).HasMaxLength(500);
            entity.Property(e => e.NewValue).HasMaxLength(500);
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.Property(e => e.ChangedBy).HasMaxLength(255);
            entity.HasOne(e => e.LabResult).WithMany().HasForeignKey(e => e.LabResultId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.LabResultId);
        });

        // AnalysisRun
        modelBuilder.Entity<AnalysisRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(e => e.ProcessorVersion).IsRequired().HasMaxLength(50);

            entity.HasIndex(e => e.MedicalReportId);
            entity.HasIndex(e => e.Status);
        });
    }
}
