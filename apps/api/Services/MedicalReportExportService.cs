using System.Text.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Services;

public sealed class MedicalReportExportService(DocumentDbContext db, IMedicalSoftwareWriter writer, IConfiguration configuration,
    ILogger<MedicalReportExportService> logger)
{
    public static bool Enabled(IConfiguration? configuration) => configuration?.GetValue<bool>("MedicalSoftwareExport:Enabled") == true;

    public async Task ExportAsync(Guid exportId, CancellationToken ct = default)
    {
        var candidate = await db.MedicalReportExports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == exportId, ct);
        if (candidate is null || candidate.Destination != MedicalReportExport.SelfUploadedDestination) return;
        var maxAttempts = Math.Clamp(configuration.GetValue("MedicalSoftwareExport:MaxAttempts", 3), 1, 10);
        var now = DateTimeOffset.UtcNow;
        if (candidate.AttemptCount >= maxAttempts || (candidate.Status != MedicalReportExportStatus.PENDING
            && !(candidate.Status == MedicalReportExportStatus.FAILED && candidate.NextAttemptAt.HasValue && candidate.NextAttemptAt <= now))) return;

        // Atomic compare-and-swap across processes. A completed or interrupted claim is never blindly replayed.
        var claimed = await db.MedicalReportExports.Where(x => x.Id == exportId && x.Status == candidate.Status && x.AttemptCount == candidate.AttemptCount)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MedicalReportExportStatus.IN_PROGRESS)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1).SetProperty(x => x.LastAttemptAt, now)
                .SetProperty(x => x.NextAttemptAt, (DateTimeOffset?)null).SetProperty(x => x.ExternalRowIdsJson, (string?)null)
                .SetProperty(x => x.LastErrorCode, "EXPORT_OUTCOME_UNCONFIRMED")
                .SetProperty(x => x.LastErrorSafeMessage, "An interrupted attempt requires reconciliation before another export."), ct);
        if (claimed != 1) return;
        var attempt = candidate.AttemptCount + 1;
        try
        {
            var report = await db.MedicalReports.AsNoTracking().Include(x => x.LabResults).SingleAsync(x => x.Id == candidate.MedicalReportId, ct);
            if (report.Status is not (ReportStatus.Completed or ReportStatus.RequiresReview) || report.DocumentType != "LAB_REPORT")
                throw new ExportDeliveryException(safeToRetry: false);
            var sourceRows = report.LabResults.OrderBy(x => x.PageNumber).ThenBy(x => x.Id).ToList();
            var rows = sourceRows.Select(SelfUploadedReportMapper.MapRow).ToList();
            var suppressed = sourceRows.Count(x => x.ValueNumeric.HasValue && !SelfUploadedReportMapper.IsRepresentable(x.ValueNumeric.Value));
            await writer.WriteAsync(rows, async ids =>
            {
                var updated = await db.MedicalReportExports.Where(x => x.Id == exportId && x.Status == MedicalReportExportStatus.IN_PROGRESS)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExternalRowIdsJson, JsonSerializer.Serialize(ids, (JsonSerializerOptions?)null))
                        .SetProperty(x => x.RowCount, rows.Count).SetProperty(x => x.SuppressedNumericCount, suppressed), ct);
                if (updated != 1) throw new InvalidOperationException("Export claim no longer active.");
            }, ct);
            // This is deliberately a separate primary write, not a cross-database transaction.
            var completedAt = DateTimeOffset.UtcNow;
            await db.MedicalReportExports.Where(x => x.Id == exportId).ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.Status, MedicalReportExportStatus.COMPLETED).SetProperty(x => x.CompletedAt, (DateTimeOffset?)completedAt)
                    .SetProperty(x => x.LastErrorCode, (string?)null).SetProperty(x => x.LastErrorSafeMessage, (string?)null), ct);
            logger.LogInformation("Medical export completed exportId={ExportId} rows={RowCount}", exportId, rows.Count);
        }
        catch (Exception ex)
        {
            // Lost COMMIT acknowledgements, cancellation or an interrupted primary completion save must NOT be replayed.
            var retryable = ex is ExportDeliveryException { SafeToRetry: true };
            DateTimeOffset? next = retryable && attempt < maxAttempts
                ? DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(configuration.GetValue("MedicalSoftwareExport:RetryBaseSeconds", 10d), .1, 300) * Math.Pow(2, attempt - 1)) : null;
            await db.MedicalReportExports.Where(x => x.Id == exportId && x.Status != MedicalReportExportStatus.COMPLETED)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MedicalReportExportStatus.FAILED).SetProperty(x => x.NextAttemptAt, next)
                    .SetProperty(x => x.LastErrorCode, retryable ? "SECONDARY_DATABASE_UNAVAILABLE" : "EXPORT_RECONCILIATION_REQUIRED")
                    .SetProperty(x => x.LastErrorSafeMessage, retryable ? "Secondary export failed safely; canonical report is retained."
                        : "Export outcome must be reconciled before retry; canonical report is retained."), CancellationToken.None);
            // Never log the exception, SQL parameters, connection string, or source medical data.
            logger.LogWarning("Medical export incomplete exportId={ExportId} retryScheduled={RetryScheduled}", exportId, next.HasValue);
        }
    }
}
