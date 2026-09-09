using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AI.DocumentReader.Tests;

public class ProcessingWorkflowTests
{
    [Fact]
    public async Task SequentialDuplicateProcessingPersistsOneResultSet()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new DocumentDbContext(new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var path = Path.Combine(Path.GetTempPath(), $"phase13b-{Guid.NewGuid():N}.pdf"); await File.WriteAllBytesAsync(path, "%PDF-1.4"u8.ToArray());
        try
        {
            var report = new MedicalReport { OriginalFileName = "synthetic.pdf", StoredFileName = "synthetic.pdf", ContentType = "application/pdf", FileSize = 8, Status = ReportStatus.Processing };
            var job = new ProcessingJob { ReportId = report.Id, Status = ProcessingJobStatus.PROCESSING, AttemptCount = 1 };
            db.AddRange(report, job); await db.SaveChangesAsync();
            var storage = new Mock<ILocalStorageService>(); storage.Setup(x => x.GetReportAbsolutePath("synthetic.pdf")).Returns(path);
            var ai = new Mock<IAiServiceClient>(); ai.Setup(x => x.AnalyseDocumentAsync(path, "synthetic.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(new AiAnalysisResponseDto(false, false, [1], [new AiLabResultDto("Hemoglobin", "Hemoglobin", 10.8m, "10.8", "g/dL", 13m, 17m, "13 - 17", 1, .9m)]));
            var service = new ReportProcessingService(db, storage.Object, ai.Object, new ReferenceRangeClassifier(), NullLogger<ReportProcessingService>.Instance);
            await service.ProcessAsync(job, default);
            await service.ProcessAsync(job, default);
            db.ChangeTracker.Clear();
            Assert.Single(await db.LabResults.Where(x => x.MedicalReportId == report.Id).ToListAsync());
            Assert.Empty(await db.ValidationIssues.ToListAsync());
            Assert.Equal(ProcessingJobStatus.COMPLETED, (await db.ProcessingJobs.SingleAsync()).Status);
        }
        finally { File.Delete(path); }
    }
}
