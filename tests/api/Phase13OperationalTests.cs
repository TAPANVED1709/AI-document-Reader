using System.Reflection;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace AI.DocumentReader.Tests;

public class Phase13OperationalTests(ITestOutputHelper output)
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"phase13e-{Guid.NewGuid():N}.db");
        public ServiceProvider Services { get; }
        public Mock<IAiServiceClient> Ai { get; } = new();
        public int Active, Peak, Calls;
        public bool Unavailable, Hold;
        public Fixture()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Processing:MaxConcurrentJobs"] = "2", ["Processing:RetryBaseSeconds"] = "4", ["Processing:StaleJobSeconds"] = "1" }).Build();
            var storage = new Mock<ILocalStorageService>();
            storage.Setup(x => x.GetReportAbsolutePath(It.IsAny<string>())).Returns("synthetic.pdf");
            Ai.Setup(x => x.AnalyseDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string path, string name, CancellationToken ct) =>
                {
                    Interlocked.Increment(ref Calls);
                    var count = Interlocked.Increment(ref Active);
                    int prior;
                    do { prior = Peak; } while (count > prior && Interlocked.CompareExchange(ref Peak, count, prior) != prior);
                    try
                    {
                        while (Hold) await Task.Delay(20, ct);
                        await Task.Delay(300, ct);
                        if (Unavailable) throw new HttpRequestException("Synthetic local AI unavailable");
                        return new AiAnalysisResponseDto(false, false, [1], [new AiLabResultDto("Hemoglobin", "Hemoglobin", 10.8m, "10.8", "g/dL", 13, 17, "13 - 17", 1, .9m,
                            ValidationIssues: [new("SYNTHETIC_NOTE", "INFO", "value", "Synthetic validation note", false)])]);
                    }
                    finally { Interlocked.Decrement(ref Active); }
                });
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddDbContext<DocumentDbContext>(o => o.UseSqlite($"Data Source={_path};Pooling=False"));
            services.AddSingleton(storage.Object); services.AddSingleton(Ai.Object);
            services.AddSingleton<IReferenceRangeClassifier, ReferenceRangeClassifier>();
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ReportProcessingService>>(NullLogger<ReportProcessingService>.Instance);
            services.AddScoped<ISecurityAuditService, SecurityAuditService>();
            services.AddScoped<IReportProcessingService, ReportProcessingService>();
            Services = services.BuildServiceProvider();
            using var scope = Services.CreateScope(); scope.ServiceProvider.GetRequiredService<DocumentDbContext>().Database.EnsureCreated();
        }
        public ProcessingWorker Worker() => new(Services.GetRequiredService<IServiceScopeFactory>(), Services.GetRequiredService<IConfiguration>(), NullLogger<ProcessingWorker>.Instance);
        public async Task<ProcessingJob> Seed()
        {
            using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            var report = new MedicalReport { OriginalFileName = "synthetic.pdf", StoredFileName = "synthetic.pdf", ContentType = "application/pdf", FileSize = 8 };
            var job = new ProcessingJob { ReportId = report.Id }; db.AddRange(report, job); await db.SaveChangesAsync(); return job;
        }
        public async Task<ProcessingJob> Read(Guid id)
        { using var scope = Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<DocumentDbContext>().ProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == id); }
        public async Task AssertOneSet(Guid reportId)
        {
            using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            Assert.Single(await db.ProcessingJobs.Where(x => x.ReportId == reportId).ToListAsync());
            Assert.Single(await db.LabResults.Where(x => x.MedicalReportId == reportId).ToListAsync());
            Assert.Single(await db.ValidationIssues.Where(x => x.LabResult!.MedicalReportId == reportId).ToListAsync());
            Assert.Single(await db.AnalysisRuns.Where(x => x.MedicalReportId == reportId).ToListAsync());
            Assert.Empty(await db.ExplanationRecords.Where(x => x.MedicalReportId == reportId).ToListAsync());
        }
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); File.Delete(_path); }
    }
    private static async Task Until(Func<Task<bool>> condition)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); while (!await condition()) await Task.Delay(20, timeout.Token); }
    private static Task<ProcessingJob?> Claim(ProcessingWorker worker) => (Task<ProcessingJob?>)typeof(ProcessingWorker).GetMethod("ClaimNext", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(worker, [CancellationToken.None])!;
    private static Task Recover(ProcessingWorker worker) => (Task)typeof(ProcessingWorker).GetMethod("RecoverStaleJobs", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(worker, [CancellationToken.None])!;

    [Fact]
    public async Task IndependentWorkersRaceForSameJobAndOnlyOneClaims()
    {
        await using var fixture = new Fixture(); var job = await fixture.Seed();
        using var workerA = fixture.Worker(); using var workerB = fixture.Worker();
        using var barrier = new Barrier(2);
        var attempts = new[] { workerA, workerB }.Select(worker => Task.Run(async () =>
        { Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10))); return await Claim(worker); })).ToArray();
        var claims = await Task.WhenAll(attempts);
        var winner = Assert.Single(claims, x => x != null)!;
        using (var scope = fixture.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IReportProcessingService>().ProcessAsync(winner, default);
        var final = await fixture.Read(job.Id);
        Assert.Equal(ProcessingJobStatus.COMPLETED, final.Status); Assert.Equal(1, final.AttemptCount);
        await fixture.AssertOneSet(job.ReportId);
        output.WriteLine("Concurrent claim attempts=2, successful=1; jobs/results/issues/analysisRuns=1/1/1/1; terminal=COMPLETED");
    }

    [Fact]
    public async Task InterruptedWorkerRecoversSameStaleJob()
    {
        await using var fixture = new Fixture { Hold = true }; var job = await fixture.Seed();
        using var first = fixture.Worker(); await first.StartAsync(default);
        await Until(async () => (await fixture.Read(job.Id)).Status == ProcessingJobStatus.PROCESSING && fixture.Active == 1);
        await first.StopAsync(default);
        Assert.Equal(ProcessingJobStatus.PROCESSING, (await fixture.Read(job.Id)).Status);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>(); var interrupted = await db.ProcessingJobs.SingleAsync();
            interrupted.StartedAt = interrupted.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-10); await db.SaveChangesAsync();
        }
        fixture.Hold = false; using var replacement = fixture.Worker(); await Recover(replacement);
        Assert.Equal(ProcessingJobStatus.RETRYING, (await fixture.Read(job.Id)).Status);
        await replacement.StartAsync(default);
        try { await Until(async () => (await fixture.Read(job.Id)).Status == ProcessingJobStatus.COMPLETED); }
        finally { await replacement.StopAsync(default); }
        Assert.Equal(2, (await fixture.Read(job.Id)).AttemptCount); await fixture.AssertOneSet(job.ReportId);
        output.WriteLine("PROCESSING -> worker cancelled -> RETRYING -> PROCESSING -> COMPLETED; same job; attempts=2; one result/issue/run");
    }

    [Fact]
    public async Task RetryRecoversBeforeBoundedAttemptsExpire()
    {
        await using var fixture = new Fixture { Unavailable = true }; var job = await fixture.Seed();
        using var worker = fixture.Worker(); await worker.StartAsync(default);
        try
        {
            await Until(async () => (await fixture.Read(job.Id)).Status == ProcessingJobStatus.RETRYING);
            var retry = await fixture.Read(job.Id); Assert.Equal(1, retry.AttemptCount); Assert.Equal("AI_SERVICE_UNAVAILABLE", retry.LastErrorCode);
            fixture.Unavailable = false;
            await Until(async () => (await fixture.Read(job.Id)).Status == ProcessingJobStatus.COMPLETED);
            var final = await fixture.Read(job.Id); Assert.Equal(2, final.AttemptCount); Assert.Null(final.LastErrorCode); Assert.Null(final.NextRetryAt);
            Assert.True(final.StartedAt >= retry.NextRetryAt); await fixture.AssertOneSet(job.ReportId);
            // The terminal state is committed before the lifecycle audit event.
            // Wait for that observable event instead of racing the worker's final save.
            await Until(async () =>
            {
                using var auditScope = fixture.Services.CreateScope();
                return await auditScope.ServiceProvider.GetRequiredService<DocumentDbContext>().SecurityAuditEvents.AnyAsync(x => x.ResourceId == job.Id && x.Action == "PROCESSING_COMPLETED");
            });
            using var scope = fixture.Services.CreateScope(); var actions = await scope.ServiceProvider.GetRequiredService<DocumentDbContext>().SecurityAuditEvents.Select(x => x.Action).ToListAsync();
            Assert.Contains("PROCESSING_RETRY", actions); Assert.Contains("PROCESSING_COMPLETED", actions);
            output.WriteLine("AI retry recovery: attempt 1 RETRYING/AI_SERVICE_UNAVAILABLE -> restore -> attempt 2 COMPLETED after NextRetryAt; one result set; safe error cleared");
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task SixQueuedJobsMeasureActiveConcurrency()
    {
        await using var fixture = new Fixture { Hold = true }; var jobs = new List<ProcessingJob>();
        for (var i = 0; i < 6; i++) jobs.Add(await fixture.Seed());
        using var worker = fixture.Worker(); await worker.StartAsync(default);
        try
        {
            await Until(() => Task.FromResult(fixture.Active == 2));
            var states = await Task.WhenAll(jobs.Select(x => fixture.Read(x.Id)));
            Assert.Equal(2, states.Count(x => x.Status == ProcessingJobStatus.PROCESSING));
            Assert.Equal(4, states.Count(x => x.Status == ProcessingJobStatus.QUEUED));
            fixture.Hold = false;
            await Until(async () => (await Task.WhenAll(jobs.Select(x => fixture.Read(x.Id)))).All(x => x.Status == ProcessingJobStatus.COMPLETED));
            Assert.Equal(2, fixture.Peak); Assert.Equal(6, fixture.Calls);
            foreach (var job in jobs) await fixture.AssertOneSet(job.ReportId);
            output.WriteLine("Runtime instrumentation: peakProcessing=2; peakQueued=6; queued while two active=4; completed=6");
        }
        finally { fixture.Hold = false; await worker.StopAsync(default); }
    }

    [Fact]
    public async Task ExhaustedStaleJobDoesNotRetryForever()
    {
        await using var fixture = new Fixture(); var job = await fixture.Seed();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>(); var row = await db.ProcessingJobs.SingleAsync();
            row.Status = ProcessingJobStatus.PROCESSING; row.AttemptCount = row.MaxAttempts; row.StartedAt = DateTimeOffset.UtcNow.AddDays(-1); await db.SaveChangesAsync();
        }
        using var worker = fixture.Worker(); await Recover(worker);
        Assert.Equal(ProcessingJobStatus.FAILED, (await fixture.Read(job.Id)).Status); Assert.Null(await Claim(worker));
    }
}
