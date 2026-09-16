using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AI.DocumentReader.Tests;

public class MedicalSoftwareExportTests
{
    private sealed class Databases : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dual-export-" + Guid.NewGuid().ToString("N"));
        public IConfiguration Config { get; } = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["MedicalSoftwareExport:Enabled"] = "true", ["MedicalSoftwareExport:RetryBaseSeconds"] = ".1", ["MedicalSoftwareExport:MaxAttempts"] = "3" }).Build();
        public DocumentDbContext Primary() => new(new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite($"Data Source={root}/primary.db;Pooling=False;Default Timeout=30").Options);
        public MedicalSoftwareDbContext Secondary() => new(new DbContextOptionsBuilder<MedicalSoftwareDbContext>().UseSqlite($"Data Source={root}/secondary.db;Pooling=False;Default Timeout=30").Options);
        public async Task Initialize()
        {
            Directory.CreateDirectory(root);
            await using var primary = Primary(); await primary.Database.EnsureCreatedAsync();
            await MedicalReportExportSchema.ApplyAsync(primary); await MedicalReportExportSchema.ApplyAsync(primary);
            await using var secondary = Secondary(); await secondary.Database.EnsureCreatedAsync();
        }
        public async Task<(Guid Report, Guid Export)> Process(List<AiLabResultDto>? rows = null, string? date = "2026-09-16")
        {
            await using var db = Primary();
            var report = new MedicalReport { OriginalFileName = "synthetic.pdf", StoredFileName = "synthetic.pdf", PatientUserId = Guid.NewGuid() };
            var job = new ProcessingJob { ReportId = report.Id, Status = ProcessingJobStatus.PROCESSING, AttemptCount = 1 };
            db.AddRange(report, job); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var storage = new Mock<ILocalStorageService>(); storage.Setup(x => x.GetReportAbsolutePath(It.IsAny<string>())).Returns("synthetic.pdf");
            var ai = new Mock<IAiServiceClient>();
            ai.Setup(x => x.AnalyseDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(
                new AiAnalysisResponseDto(false, false, [1], rows ?? [Row("Hemoglobin", 13.4m, "g/dL", 12, 15), Row("Creatinine", 1.1m, "mg/dL", .7m, 1.3m)],
                    DocumentType: "LAB_REPORT", StructuredData: new() { ["report"] = new { reportGeneratedDate = date, bookingDate = "2026-09-14", collectionDate = "2026-09-15" } }));
            var processor = new ReportProcessingService(db, storage.Object, ai.Object, new ReferenceRangeClassifier(), NullLogger<ReportProcessingService>.Instance, configuration: Config);
            await processor.ProcessAsync(job, default); db.ChangeTracker.Clear();
            // A retry of canonical processing must not add another export intent or alter existing rows.
            await processor.ProcessAsync(await db.ProcessingJobs.SingleAsync(), default); db.ChangeTracker.Clear();
            var export = await db.MedicalReportExports.SingleAsync();
            Assert.Equal(MedicalReportExportStatus.PENDING, export.Status);
            Assert.NotNull((await db.MedicalReports.SingleAsync()).PatientUserId);
            return (report.Id, export.Id);
        }
        public async Task Export(Guid id, IMedicalSoftwareWriter? writer = null)
        {
            await using var db = Primary(); await using var secondary = Secondary();
            await new MedicalReportExportService(db, writer ?? new MedicalSoftwareWriter(secondary), Config, NullLogger<MedicalReportExportService>.Instance).ExportAsync(id);
        }
        public async Task MakeRetryDue(Guid id)
        {
            await using var db = Primary();
            var due = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.MedicalReportExports.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, (DateTimeOffset?)due));
        }
        public ValueTask DisposeAsync()
        {
            foreach (var file in Directory.GetFiles(root)) File.Delete(file);
            Directory.Delete(root);
            return ValueTask.CompletedTask;
        }
    }

    private static AiLabResultDto Row(string name, decimal value, string unit, decimal min, decimal max) =>
        new(name, "Normalized " + name, value, value.ToString(CultureInfo.InvariantCulture), unit, min, max,
            $"{min.ToString(CultureInfo.InvariantCulture)}-{max.ToString(CultureInfo.InvariantCulture)}", 1, .99m, ReferenceType: "BETWEEN");

    [Fact]
    public async Task HappyPathCommitsBothDatabasesAndNeverDuplicatesCompletedExport()
    {
        await using var fixture = new Databases(); await fixture.Initialize(); var (reportId, exportId) = await fixture.Process();
        await fixture.Export(exportId); await fixture.Export(exportId);
        await using var db = fixture.Primary(); await using var secondary = fixture.Secondary();
        var report = await db.MedicalReports.Include(x => x.LabResults).SingleAsync();
        Assert.Equal(reportId, report.Id); Assert.Equal(2, report.LabResults.Count);
        Assert.Equal(ProcessingJobStatus.COMPLETED, (await db.ProcessingJobs.SingleAsync()).Status);
        var rows = await secondary.SelfUploadedReportData.ToListAsync(); Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.TestName == "Hemoglobin" && x.Result == 13.4m && x.Unit == "g/dL" && x.ReferenceRange == "12-15");
        Assert.Contains(rows, x => x.TestName == "Creatinine" && x.Result == 1.1m && x.Unit == "mg/dL" && x.ReferenceRange == "0.7-1.3");
        var export = await db.MedicalReportExports.SingleAsync(); Assert.Equal(MedicalReportExportStatus.COMPLETED, export.Status); Assert.Equal(1, export.AttemptCount);
        Assert.Equal(2, JsonSerializer.Deserialize<long[]>(export.ExternalRowIdsJson!)!.Length);
        var response = SelfUploadedReportMapper.Map(report); Assert.Equal("2026-09-16", response.ReportGeneratedDate); Assert.Equal(2, response.Results.Count);
    }

    [Fact]
    public async Task QualitativeAndUnsafeValuesStayInPrimaryAndExportNullWithoutLosingNeighbors()
    {
        await using var fixture = new Databases(); await fixture.Initialize();
        var (_, id) = await fixture.Process([
            Row("Hemoglobin", 13.4m, "g/dL", 12, 15),
            new("HBsAg", "HBsAg", null, "Non-Reactive", null, null, null, "Non-Reactive", 1, .99m, ReferenceType: "TEXT_ONLY"),
            Row("Extreme OCR", 6392437665405192000m, "mg/dL", 0, 10),
            Row("Primary safe but external unsafe", 10000000000000m, "mg/dL", 0, 10)]);
        await fixture.Export(id);
        await using var db = fixture.Primary(); await using var secondary = fixture.Secondary();
        var primary = await db.LabResults.ToListAsync(); Assert.Equal(4, primary.Count);
        var bad = primary.Single(x => x.OriginalTestName == "Extreme OCR"); Assert.Null(bad.ValueNumeric); Assert.True(bad.ReviewRequired); Assert.Contains("NUMERIC_OUT_OF_RANGE", bad.AmbiguityReason);
        Assert.Equal("6392437665405192000", bad.ValueText);
        Assert.Equal(10000000000000m, primary.Single(x => x.OriginalTestName.StartsWith("Primary safe")).ValueNumeric);
        Assert.Equal("Non-Reactive", primary.Single(x => x.OriginalTestName == "HBsAg").ValueText);
        var external = await secondary.SelfUploadedReportData.ToListAsync(); Assert.Equal(4, external.Count);
        Assert.All(external.Where(x => x.TestName != "Hemoglobin"), x => Assert.Null(x.Result));
        Assert.Equal(13.4m, external.Single(x => x.TestName == "Hemoglobin").Result);
        Assert.Equal(1, (await db.MedicalReportExports.SingleAsync()).SuppressedNumericCount);
        Assert.Equal(ProcessingJobStatus.REVIEW_REQUIRED, (await db.ProcessingJobs.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("9999999999999.99999", true)]
    [InlineData("-9999999999999.99999", true)]
    [InlineData("10000000000000", false)]
    [InlineData("-10000000000000", false)]
    [InlineData("0.000001", false)]
    [InlineData("6392437665405192000", false)]
    [InlineData("13.400000", true)]
    public void ExternalDecimalRangeAndScaleAreExact(string text, bool safe)
    {
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);
        Assert.Equal(safe, SelfUploadedReportMapper.IsRepresentable(value));
        Assert.Equal(safe ? value : (decimal?)null, SelfUploadedReportMapper.SafeNumeric(value));
    }

    private sealed class FailureWriter(bool safe) : IMedicalSoftwareWriter
    {
        public Task WriteAsync(IReadOnlyList<SelfUploadedReportData> rows, Func<IReadOnlyList<long>, Task> prepared, CancellationToken ct) => throw new ExportDeliveryException(safe);
    }

    [Fact]
    public async Task SecondaryOutageRetainsPrimaryThenRetriesWithoutReuploadAndStopsAtBound()
    {
        await using var fixture = new Databases(); await fixture.Initialize(); var (_, id) = await fixture.Process();
        await fixture.Export(id, new FailureWriter(true));
        await using (var db = fixture.Primary())
        {
            Assert.Equal(1, await db.MedicalReports.CountAsync()); Assert.Equal(2, await db.LabResults.CountAsync());
            Assert.Equal(ProcessingJobStatus.COMPLETED, (await db.ProcessingJobs.SingleAsync()).Status);
            var state = await db.MedicalReportExports.SingleAsync(); Assert.Equal(MedicalReportExportStatus.FAILED, state.Status);
            Assert.Equal("SECONDARY_DATABASE_UNAVAILABLE", state.LastErrorCode); Assert.NotNull(state.NextAttemptAt);
        }
        await fixture.MakeRetryDue(id); await fixture.Export(id); await fixture.Export(id);
        await using (var db = fixture.Primary()) Assert.Equal(2, (await db.MedicalReportExports.SingleAsync()).AttemptCount);
        await using (var secondary = fixture.Secondary()) Assert.Equal(2, await secondary.SelfUploadedReportData.CountAsync());

        // A separate fixture verifies that repeated outages are bounded, not an infinite retry loop.
        await using var down = new Databases(); await down.Initialize(); var (_, failedId) = await down.Process();
        for (var i = 0; i < 3; i++) { if (i > 0) await down.MakeRetryDue(failedId); await down.Export(failedId, new FailureWriter(true)); }
        await down.Export(failedId);
        await using var failedDb = down.Primary(); var failed = await failedDb.MedicalReportExports.SingleAsync();
        Assert.Equal(3, failed.AttemptCount); Assert.Null(failed.NextAttemptAt); Assert.Equal(MedicalReportExportStatus.FAILED, failed.Status);
    }

    private sealed class CommitThenLoseAcknowledgement(IMedicalSoftwareWriter writer) : IMedicalSoftwareWriter
    {
        public async Task WriteAsync(IReadOnlyList<SelfUploadedReportData> rows, Func<IReadOnlyList<long>, Task> prepared, CancellationToken ct)
        { await writer.WriteAsync(rows, prepared, ct); throw new ExportDeliveryException(false); }
    }

    [Fact]
    public async Task LostCommitAcknowledgementDoesNotReplayCommittedRows()
    {
        await using var fixture = new Databases(); await fixture.Initialize(); var (_, id) = await fixture.Process();
        await using var secondary = fixture.Secondary();
        await fixture.Export(id, new CommitThenLoseAcknowledgement(new MedicalSoftwareWriter(secondary)));
        await fixture.Export(id);
        Assert.Equal(2, await secondary.SelfUploadedReportData.CountAsync());
        await using var db = fixture.Primary(); var state = await db.MedicalReportExports.SingleAsync();
        Assert.Equal("EXPORT_RECONCILIATION_REQUIRED", state.LastErrorCode); Assert.Null(state.NextAttemptAt); Assert.Equal(1, state.AttemptCount);
        Assert.NotNull(state.ExternalRowIdsJson); Assert.Equal(ProcessingJobStatus.COMPLETED, (await db.ProcessingJobs.SingleAsync()).Status);
    }

    private sealed class BlockingWriter(IMedicalSoftwareWriter writer) : IMedicalSoftwareWriter
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WriteAsync(IReadOnlyList<SelfUploadedReportData> rows, Func<IReadOnlyList<long>, Task> prepared, CancellationToken ct)
        { Entered.SetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); await writer.WriteAsync(rows, prepared, ct); }
    }

    [Fact]
    public async Task ConcurrentWorkersClaimOnlyOnce()
    {
        await using var fixture = new Databases(); await fixture.Initialize(); var (_, id) = await fixture.Process();
        await using var secondary = fixture.Secondary(); var gate = new BlockingWriter(new MedicalSoftwareWriter(secondary));
        var first = fixture.Export(id, gate); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await fixture.Export(id); } finally { gate.Release.SetResult(); }
        await first; Assert.Equal(2, await secondary.SelfUploadedReportData.CountAsync());
        await using var db = fixture.Primary(); Assert.Equal(1, (await db.MedicalReportExports.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task FailureBeforeCommitRollsBackAllSecondaryRows()
    {
        await using var fixture = new Databases(); await fixture.Initialize(); await using var secondary = fixture.Secondary();
        var failure = await Assert.ThrowsAsync<ExportDeliveryException>(() => new MedicalSoftwareWriter(secondary).WriteAsync(
            [new() { TestName = "Synthetic", Result = 13.4m }], _ => throw new IOException("Synthetic unavailable primary ledger"), default));
        Assert.True(failure.SafeToRetry); Assert.Equal(0, await secondary.SelfUploadedReportData.CountAsync());
    }

    [Fact]
    public async Task UniquePrimaryLedgerAndExternalSchemaAreSeparate()
    {
        await using var fixture = new Databases(); await fixture.Initialize(); var (reportId, _) = await fixture.Process();
        await using var db = fixture.Primary(); db.MedicalReportExports.Add(new() { MedicalReportId = reportId });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Null(db.Model.FindEntityType(typeof(SelfUploadedReportData)));
        await using var external = fixture.Secondary();
        var entity = external.Model.FindEntityType(typeof(SelfUploadedReportData))!;
        Assert.Equal(5, entity.GetProperties().Count()); Assert.Equal(18, entity.FindProperty("Result")!.GetPrecision()); Assert.Equal(5, entity.FindProperty("Result")!.GetScale());
    }

    [Theory]
    [InlineData("2026-09-16")]
    [InlineData(null)]
    public async Task AuthenticatedExportDtoPreservesDatesNullsSourceNamesAndPatientOwnership(string? generated)
    {
        await using var factory = new Phase10HttpSecurityFactory(); await factory.SeedAsync();
        var (client, patient) = await factory.LoginAsync("patient-a@test"); Guid reportId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            var report = new MedicalReport { PatientUserId = patient.Id, ReportDate = DateTimeOffset.UtcNow,
                StructuredDataJson = JsonSerializer.Serialize(new { report = new { reportGeneratedDate = generated, bookingDate = "2026-09-14", collectionDate = "2026-09-15" } }) };
            reportId = report.Id; report.LabResults.Add(new() { OriginalTestName = "Source HBsAg", NormalizedTestName = "HBsAg", ValueNumeric = null, ValueText = "Non-Reactive" });
            db.Add(report); await db.SaveChangesAsync();
        }
        var response = await client.GetFromJsonAsync<JsonElement>($"/api/reports/{reportId}/medical-software");
        Assert.Equal(generated, response.GetProperty("reportGeneratedDate").GetString());
        var row = response.GetProperty("results")[0]; Assert.Equal("Source HBsAg", row.GetProperty("testName").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("result").ValueKind);
        var (other, _) = await factory.LoginAsync("patient-b@test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/reports/{reportId}/medical-software")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/reports/{reportId}/medical-software/status")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/reports/{reportId}/medical-software")).StatusCode);
    }
}
