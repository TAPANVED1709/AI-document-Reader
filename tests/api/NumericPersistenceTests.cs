using System.Globalization;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AI.DocumentReader.Tests;

public class NumericPersistenceTests
{
    private sealed class EnforceSqlDecimalLimits : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            foreach (var entry in data.Context!.ChangeTracker.Entries<LabResult>().Where(e => e.State is EntityState.Added or EntityState.Modified))
                foreach (var property in entry.Properties.Where(p => p.Metadata.GetPrecision() == 18))
                    if (property.CurrentValue is decimal number && LabNumericSafety.IssueCode(number, property.Metadata.GetScale() == 2) != null)
                        throw new DbUpdateException("Synthetic SQL decimal range/scale failure");
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("value")]
    [InlineData("referenceMin")]
    [InlineData("referenceMax")]
    public async Task ExtremeCandidateIsIsolatedWithoutLosingAnyOf74Rows(string field)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new DocumentDbContext(new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection).AddInterceptors(new EnforceSqlDecimalLimits()).Options);
        await db.Database.EnsureCreatedAsync();
        var report = new MedicalReport { OriginalFileName = "synthetic.pdf", StoredFileName = "synthetic.pdf" };
        var job = new ProcessingJob { ReportId = report.Id, Status = ProcessingJobStatus.PROCESSING, AttemptCount = 1 };
        db.AddRange(report, job); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        const decimal extreme = 6392437665405192000.0000m;
        var rows = Enumerable.Range(0, 74).Select(i => new AiLabResultDto($"Synthetic {i}", null, 10.8m, "10.8", "g/dL", 13, 17, "13 - 17", 1, .99m, ReferenceType: "BETWEEN")).ToList();
        rows[5] = rows[5] with { Value = field == "value" ? extreme : 10.8m,
            ReferenceMin = field == "referenceMin" ? extreme : 13,
            ReferenceMax = field == "referenceMax" ? extreme : 17,
            ValueText = "6392437665405192000.0000", ReferenceText = "0 - 6392437665405192000.0000" };
        var storage = new Mock<ILocalStorageService>(); storage.Setup(s => s.GetReportAbsolutePath(It.IsAny<string>())).Returns("synthetic.pdf");
        var ai = new Mock<IAiServiceClient>(); ai.Setup(s => s.AnalyseDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AiAnalysisResponseDto(false, false, [1], rows));
        var service = new ReportProcessingService(db, storage.Object, ai.Object, new ReferenceRangeClassifier(), NullLogger<ReportProcessingService>.Instance);
        await service.ProcessAsync(job, default); db.ChangeTracker.Clear();
        var saved = await db.LabResults.Include(r => r.ValidationIssues).ToListAsync();
        Assert.Equal(74, saved.Count);
        var bad = saved.Single(r => r.OriginalTestName == "Synthetic 5");
        Assert.Null(field == "value" ? bad.ValueNumeric : field == "referenceMin" ? bad.ReferenceMin : bad.ReferenceMax);
        Assert.Equal("6392437665405192000.0000", bad.ValueText);
        Assert.Equal("0 - 6392437665405192000.0000", bad.ReferenceText);
        Assert.True(bad.ReviewRequired); Assert.Contains("NUMERIC_OUT_OF_RANGE", bad.AmbiguityReason);
        Assert.Contains(bad.ValidationIssues, i => i.Code == "NUMERIC_OUT_OF_RANGE" && i.RequiresReview);
        Assert.Equal(ResultStatus.UNKNOWN, bad.CalculatedStatus);
        Assert.All(saved.Where(r => r.Id != bad.Id), r => { Assert.Equal(10.8m, r.ValueNumeric); Assert.False(r.ReviewRequired); });
        Assert.Equal(ReportStatus.RequiresReview, (await db.MedicalReports.SingleAsync()).Status);
        var persistedJob = await db.ProcessingJobs.SingleAsync();
        Assert.Equal(ProcessingJobStatus.REVIEW_REQUIRED, persistedJob.Status); Assert.Null(persistedJob.LastErrorCode);
        // Idempotent reprocessing preserves already corrected and verified rows.
        bad.CorrectedValueNumeric = 11.2m; bad.CorrectedReferenceMin = 12; bad.CorrectedReferenceMax = 18;
        bad.IsVerified = true; bad.CorrectedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await service.ProcessAsync(persistedJob, default); db.ChangeTracker.Clear();
        var preserved = await db.LabResults.SingleAsync(r => r.Id == bad.Id);
        Assert.Equal(11.2m, preserved.CorrectedValueNumeric); Assert.Equal(12m, preserved.CorrectedReferenceMin); Assert.Equal(18m, preserved.CorrectedReferenceMax);
        Assert.True(preserved.IsVerified); Assert.Equal(74, await db.LabResults.CountAsync());
    }

    [Theory]
    [InlineData("99999999999999.9999", false, null)]
    [InlineData("-99999999999999.9999", false, null)]
    [InlineData("100000000000000.0000", false, "NUMERIC_OUT_OF_RANGE")]
    [InlineData("-100000000000000.0000", false, "NUMERIC_OUT_OF_RANGE")]
    [InlineData("9999999999999999.99", true, null)]
    [InlineData("10000000000000000.00", true, "NUMERIC_OUT_OF_RANGE")]
    [InlineData("0.00001", false, "NUMERIC_PRECISION_UNSUPPORTED")]
    [InlineData("10.801", true, "NUMERIC_PRECISION_UNSUPPORTED")]
    public void ExactStorageBoundaries(string text, bool corrected, string? expected)
        => Assert.Equal(expected, LabNumericSafety.IssueCode(decimal.Parse(text, CultureInfo.InvariantCulture), corrected));

    [Fact]
    public async Task LargestSafeExtractedDecimalPersistsExactly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new DocumentDbContext(new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection).AddInterceptors(new EnforceSqlDecimalLimits()).Options);
        await db.Database.EnsureCreatedAsync();
        var report = new MedicalReport { OriginalFileName = "synthetic.pdf", StoredFileName = "synthetic.pdf" };
        var row = new LabResult { MedicalReportId = report.Id, OriginalTestName = "Synthetic", ValueNumeric = LabNumericSafety.ExtractedMaximum, ReferenceMin = -LabNumericSafety.ExtractedMaximum, ReferenceMax = LabNumericSafety.ExtractedMaximum, ValueText = "99999999999999.9999" };
        LabNumericSafety.SanitizeExtracted(row); db.AddRange(report, row); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var saved = await db.LabResults.SingleAsync();
        Assert.Equal(LabNumericSafety.ExtractedMaximum, saved.ValueNumeric); Assert.False(saved.ReviewRequired);
        Assert.Equal(-LabNumericSafety.ExtractedMaximum, saved.ReferenceMin); Assert.Equal(LabNumericSafety.ExtractedMaximum, saved.ReferenceMax);
    }
}
