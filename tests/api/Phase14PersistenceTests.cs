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

public class Phase14PersistenceTests
{
    // Real Phase 14 SQL Server rejected an overlong reconstructed ReferenceText.
    // This interceptor models a repeatable insert failure, including a second
    // failure if poisoned tracked result entities are retried in the error path.
    private sealed class RejectResultInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<LabResult>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("Synthetic result insert rejected");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task ResultInsertFailurePersistsTerminalFailureWithoutRetryingInvalidRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection)
            .AddInterceptors(new RejectResultInsert()).Options;
        await using var db = new DocumentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var report = new MedicalReport { OriginalFileName = "synthetic.pdf", StoredFileName = "synthetic.pdf", ContentType = "application/pdf" };
        var job = new ProcessingJob { ReportId = report.Id, Status = ProcessingJobStatus.PROCESSING, AttemptCount = 1 };
        db.AddRange(report, job); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var storage = new Mock<ILocalStorageService>(); storage.Setup(s => s.GetReportAbsolutePath(It.IsAny<string>())).Returns("synthetic.pdf");
        var ai = new Mock<IAiServiceClient>();
        ai.Setup(s => s.AnalyseDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAnalysisResponseDto(false, false, [1], [new AiLabResultDto("Hemoglobin", "Hemoglobin", 10.8m, "10.8", "g/dL", 13, 17, "13 - 17", 1, .9m,
                ValidationIssues: [new("SYNTHETIC", "WARNING", "ReferenceText", "Synthetic issue", true)])]));
        var service = new ReportProcessingService(db, storage.Object, ai.Object, new ReferenceRangeClassifier(), NullLogger<ReportProcessingService>.Instance);
        await service.ProcessAsync(job, default);
        db.ChangeTracker.Clear();
        var persisted = await db.ProcessingJobs.SingleAsync();
        Assert.Equal(ProcessingJobStatus.FAILED, persisted.Status);
        Assert.NotNull(persisted.CompletedAt);
        Assert.Equal(ReportStatus.Failed, (await db.MedicalReports.SingleAsync()).Status);
        Assert.Empty(await db.LabResults.ToListAsync());
        Assert.Empty(await db.ValidationIssues.ToListAsync());
        Assert.Equal(AnalysisStatus.Failed, (await db.AnalysisRuns.SingleAsync()).Status);
    }
}
