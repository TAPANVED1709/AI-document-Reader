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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
            entity.Property(e => e.ReferenceText).HasMaxLength(100);
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
