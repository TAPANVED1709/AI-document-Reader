using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Services;

public sealed class ProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProcessingWorker> _logger;
    private readonly string _workerId = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";

    public ProcessingWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ProcessingWorker> logger)
    { _scopeFactory = scopeFactory; _configuration = configuration; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverStaleJobs(stoppingToken);
        var max = Math.Clamp(_configuration.GetValue("Processing:MaxConcurrentJobs", 2), 1, 8);
        using var gate = new SemaphoreSlim(max, max);
        var active = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var started = 0;
                for (var i = 0; i < max; i++)
                {
                    await gate.WaitAsync(stoppingToken);
                    ProcessingJob? job;
                    try { job = await ClaimNext(stoppingToken); }
                    catch { gate.Release(); throw; }
                    if (job is null) { gate.Release(); break; }
                    started++;
                    active.RemoveAll(task => task.IsCompleted);
                    active.Add(Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var service = scope.ServiceProvider.GetRequiredService<IReportProcessingService>();
                            var timeout = TimeSpan.FromSeconds(_configuration.GetValue("Processing:MaxDurationSeconds", 300));
                            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                            cts.CancelAfter(timeout);
                            await service.ProcessAsync(job, cts.Token);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* Leave the interrupted claim for stale recovery. */ }
                        catch (OperationCanceledException) { await MarkTimeout(job.Id); _logger.LogWarning("Processing timeout jobId={JobId}", job.Id); }
                        catch (Exception ex) { _logger.LogError(ex, "Unhandled worker failure jobId={JobId}", job.Id); }
                        finally { gate.Release(); }
                    }));
                }
                if (started == 0) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        finally { await Task.WhenAll(active); }
    }

    private async Task<ProcessingJob?> ClaimNext(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.ProcessingJobs.AsNoTracking().Where(x => x.Status == ProcessingJobStatus.QUEUED || x.Status == ProcessingJobStatus.RETRYING).ToListAsync(ct);
        var candidate = candidates.Where(x => x.NextRetryAt is null || x.NextRetryAt <= now).OrderBy(x => x.CreatedAt).FirstOrDefault();
        if (candidate is null) return null;
        var updated = await db.ProcessingJobs.Where(x => x.Id == candidate.Id && (x.Status == ProcessingJobStatus.QUEUED || x.Status == ProcessingJobStatus.RETRYING))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ProcessingJobStatus.PROCESSING).SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                .SetProperty(x => x.StartedAt, now).SetProperty(x => x.HeartbeatAt, now).SetProperty(x => x.WorkerId, _workerId), ct);
        if (updated != 1) return null;
        return await db.ProcessingJobs.AsNoTracking().FirstAsync(x => x.Id == candidate.Id, ct);
    }

    private async Task RecoverStaleJobs(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        var age = TimeSpan.FromSeconds(_configuration.GetValue("Processing:StaleJobSeconds", 600));
        var cutoff = DateTimeOffset.UtcNow.Subtract(age);
        var stale = await db.ProcessingJobs.Where(x => x.Status == ProcessingJobStatus.PROCESSING).ToListAsync(ct);
        foreach (var job in stale.Where(x => (x.HeartbeatAt ?? x.StartedAt ?? x.CreatedAt) < cutoff))
        {
            job.LastErrorCode = "WORKER_INTERRUPTED";
            job.LastErrorSafeMessage = "Processing was interrupted before completion.";
            if (job.AttemptCount < job.MaxAttempts)
            { job.Status = ProcessingJobStatus.RETRYING; job.NextRetryAt = DateTimeOffset.UtcNow; }
            else
            { job.Status = ProcessingJobStatus.FAILED; job.CompletedAt = DateTimeOffset.UtcNow; }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkTimeout(Guid jobId)
    {
        using var scope = _scopeFactory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        var job = await db.ProcessingJobs.FirstOrDefaultAsync(x => x.Id == jobId);
        if (job is null) return;
        job.LastErrorCode = "PROCESSING_TIMEOUT"; job.LastErrorSafeMessage = "Processing exceeded the configured time limit.";
        if (job.AttemptCount < job.MaxAttempts) { job.Status = ProcessingJobStatus.RETRYING; job.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, job.AttemptCount)); }
        else { job.Status = ProcessingJobStatus.FAILED; job.CompletedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync();
    }
}
