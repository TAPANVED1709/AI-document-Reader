using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Services;

public sealed class MedicalReportExportWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<MedicalReportExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!MedicalReportExportService.Enabled(configuration)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
                var now = DateTimeOffset.UtcNow;
                // Interrupted claims cannot be reclaimed: the supplied external schema cannot deduplicate a lost COMMIT acknowledgement.
                var active = await db.MedicalReportExports.AsNoTracking().Where(x => x.Status == MedicalReportExportStatus.IN_PROGRESS).ToListAsync(stoppingToken);
                foreach (var row in active.Where(x => x.LastAttemptAt < now.AddMinutes(-5)))
                    await db.MedicalReportExports.Where(x => x.Id == row.Id && x.Status == MedicalReportExportStatus.IN_PROGRESS)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MedicalReportExportStatus.FAILED)
                            .SetProperty(x => x.NextAttemptAt, (DateTimeOffset?)null).SetProperty(x => x.LastErrorCode, "EXPORT_RECONCILIATION_REQUIRED")
                            .SetProperty(x => x.LastErrorSafeMessage, "An interrupted export requires reconciliation before retry."), stoppingToken);
                var candidates = await db.MedicalReportExports.AsNoTracking().Where(x => x.Status == MedicalReportExportStatus.PENDING
                    || (x.Status == MedicalReportExportStatus.FAILED && x.NextAttemptAt != null)).ToListAsync(stoppingToken);
                var candidate = candidates.Where(x => x.NextAttemptAt is null || x.NextAttemptAt <= now).OrderBy(x => x.CreatedAt).FirstOrDefault();
                if (candidate is not null)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(90));
                    await scope.ServiceProvider.GetRequiredService<MedicalReportExportService>().ExportAsync(candidate.Id, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("Medical export worker unavailable; canonical processing is unaffected."); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
