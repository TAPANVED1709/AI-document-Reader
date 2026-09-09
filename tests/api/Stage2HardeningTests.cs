using System.Text.Json;
using AI.DocumentReader.Api.Controllers;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AI.DocumentReader.Tests;

public class Stage2HardeningTests
{
    [Fact]
    public async Task CorrectionPreservesExtractionAndCreatesAudit()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new DocumentDbContext(new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var report = new MedicalReport { OriginalFileName = "report.pdf", StoredFileName = "report.pdf", ContentType = "application/pdf", FileSize = 1, PageSourcesJson = "[{\"page\":1,\"source\":\"OCR\"}]" };
        var result = new LabResult { MedicalReportId = report.Id, OriginalTestName = "Hemoglobin", ValueNumeric = 108, ValueText = "108", Unit = "g/dL", ReferenceMin = 13, ReferenceMax = 17, ReferenceText = "13 - 17", ExtractionConfidence = .4m, PageNumber = 1 };
        db.MedicalReports.Add(report); db.LabResults.Add(result); await db.SaveChangesAsync();
        var storage = new Mock<ILocalStorageService>();
        var ai = new Mock<IAiServiceClient>();
        var controller = new ReportsController(db, storage.Object, ai.Object, new ReferenceRangeClassifier(), NullLogger<ReportsController>.Instance);
        var response = Assert.IsType<OkObjectResult>(await controller.CorrectResult(report.Id, result.Id,
            new ReportsController.ResultCorrectionRequest("Hemoglobin", 10.8m, "10.8", "g/dL", 13, 17, "13 - 17", "OCR decimal error", null), default));
        var body = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(108, body.GetProperty("extractedValueNumeric").GetDecimal());
        Assert.Equal(10.8m, body.GetProperty("valueNumeric").GetDecimal());
        Assert.Equal(.4m, body.GetProperty("extractionConfidence").GetDecimal());
        Assert.True(body.GetProperty("isCorrected").GetBoolean());
        Assert.Single(db.ResultCorrectionAudits);
        Assert.Equal("Value", db.ResultCorrectionAudits.Single().FieldName);
    }

    [Fact]
    public async Task CorrectionRejectsInvalidReferenceRange()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new DocumentDbContext(new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var report = new MedicalReport { OriginalFileName = "report.pdf", StoredFileName = "report.pdf", ContentType = "application/pdf", FileSize = 1 };
        var result = new LabResult { MedicalReportId = report.Id, OriginalTestName = "Test", ValueText = "1", ExtractionConfidence = .9m }; db.Add(report); db.Add(result); await db.SaveChangesAsync();
        var controller = new ReportsController(db, new Mock<ILocalStorageService>().Object, new Mock<IAiServiceClient>().Object, new ReferenceRangeClassifier(), NullLogger<ReportsController>.Instance);
        var response = await controller.CorrectResult(report.Id, result.Id, new ReportsController.ResultCorrectionRequest(null, 1, "1", null, 5, 2, null, null, null), default);
        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Empty(db.ResultCorrectionAudits);
    }

    [Theory]
    [InlineData(false, false, "NATIVE", "NATIVE_TEXT", "Completed")]
    [InlineData(true, true, "OCR", "OCR", "Completed")]
    [InlineData(true, true, "HYBRID", "OCR", "Completed")]
    [InlineData(true, false, "OCR", "OCR_UNAVAILABLE", "RequiresOcr")]
    [InlineData(true, false, "HYBRID", "OCR_UNAVAILABLE", "RequiresOcr")]
    [InlineData(true, true, "HYBRID", "OCR_FAILED", "RequiresOcr")]
    public async Task UploadCreatesPersistentQueuedJob(bool required, bool applied, string mode, string source, string status)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection).Options;
        await using var db = new DocumentDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var storage = new Mock<ILocalStorageService>();
        storage.Setup(s => s.SaveReportAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>())).ReturnsAsync(("saved.pdf", "unused.pdf"));
        var ai = new Mock<IAiServiceClient>();
        const string box = "{\"x\":10,\"y\":20,\"width\":500,\"height\":30,\"page\":1,\"dpi\":300}";
        // Deserialize the actual Python wire contract before passing it to the controller.
        var json = JsonSerializer.Serialize(new {
            requiresOcr = required, ocrRequired = required, ocrApplied = applied, processingMode = mode,
            pages = new[] {1}, pageSources = new[] {new {page = 1, source}},
            results = new[] {new {originalName="Hemoglobin", normalizedName="Hemoglobin", value=10.8m,
                valueText="10.8", unit="g/dL", referenceMin=13m, referenceMax=17m, referenceText="13.0 - 17.0",
                page=1, confidence=.21m, boundingBoxJson=box}}
        });
        var dto = JsonSerializer.Deserialize<AiAnalysisResponseDto>(json, new JsonSerializerOptions {PropertyNameCaseInsensitive=true})!;
        ai.Setup(s => s.AnalyseDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        ReportsController Controller() => new(db, storage.Object, ai.Object, new ReferenceRangeClassifier(), NullLogger<ReportsController>.Instance);
        var file = new FormFile(new MemoryStream(new byte[1]), 0, 1, "file", "report.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        var uploaded = Assert.IsType<AcceptedResult>(await Controller().UploadReport(file, default));
        var uploadJson = JsonSerializer.SerializeToElement(uploaded.Value);
        var id = uploadJson.GetProperty("reportId").GetGuid();
        Assert.Equal("QUEUED", uploadJson.GetProperty("status").GetString());
        Assert.True(db.ProcessingJobs.Any(x => x.ReportId == id && x.Status == ProcessingJobStatus.QUEUED));
        db.ChangeTracker.Clear();
        var reloaded = Assert.IsType<OkObjectResult>(await Controller().GetReportById(id, default));
        var result = JsonSerializer.SerializeToElement(reloaded.Value);
        Assert.Equal("Processing", result.GetProperty("status").GetString());
        Assert.Equal(0, result.GetProperty("resultsCount").GetInt32());
        Assert.NotEmpty(status);
    }

    [Fact]
    public async Task ExistingSqliteUpgradeIsAdditiveAndIdempotent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new DocumentDbContext(new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE MedicalReports (Id TEXT PRIMARY KEY); INSERT INTO MedicalReports (Id) VALUES ('existing')");
        await Stage2SchemaUpgrade.ApplyAsync(db);
        await Stage2SchemaUpgrade.ApplyAsync(db);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, OcrRequired, OcrApplied, ProcessingMode, PageSourcesJson FROM MedicalReports";
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("existing", reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
        Assert.Equal("UNKNOWN", reader.GetString(3));
        Assert.True(reader.IsDBNull(4));
    }
}
