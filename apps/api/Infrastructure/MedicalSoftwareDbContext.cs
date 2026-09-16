using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Infrastructure;

public sealed class SelfUploadedReportData
{
    public long Id { get; set; }
    public string? TestName { get; set; }
    public decimal? Result { get; set; }
    public string? Unit { get; set; }
    public string? ReferenceRange { get; set; }
}

public sealed class MedicalSoftwareDbContext(DbContextOptions<MedicalSoftwareDbContext> options) : DbContext(options)
{
    public DbSet<SelfUploadedReportData> SelfUploadedReportData => Set<SelfUploadedReportData>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var row = model.Entity<SelfUploadedReportData>();
        row.ToTable("SelfUploadedReportData", "dbo");
        row.HasKey(x => x.Id);
        row.Property(x => x.Id).ValueGeneratedOnAdd();
        row.Property(x => x.Result).HasPrecision(18, 5);
        // Unbounded nullable strings map to nvarchar(max) on SQL Server; allow the relational test provider's native text type.
    }
}
