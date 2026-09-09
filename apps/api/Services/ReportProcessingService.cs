using System.Diagnostics;
using System.Text.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Services;

public interface IReportProcessingService
{
    Task ProcessAsync(ProcessingJob job, CancellationToken cancellationToken);
}

public sealed class ReportProcessingService : IReportProcessingService
{
    private readonly DocumentDbContext _db;
    private readonly ILocalStorageService _storage;
    private readonly IAiServiceClient _ai;
    private readonly IReferenceRangeClassifier _classifier;
    private readonly ILogger<ReportProcessingService> _logger;
    private readonly ISecurityAuditService? _audit;
    private readonly IConfiguration? _configuration;

    public ReportProcessingService(DocumentDbContext db, ILocalStorageService storage, IAiServiceClient ai,
        IReferenceRangeClassifier classifier, ILogger<ReportProcessingService> logger, ISecurityAuditService? audit = null, IConfiguration? configuration = null)
    { _db = db; _storage = storage; _ai = ai; _classifier = classifier; _logger = logger; _audit = audit; _configuration = configuration; }

    public async Task ProcessAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        _db.ProcessingJobs.Attach(job);
        var report = await _db.MedicalReports.FirstAsync(x => x.Id == job.ReportId, cancellationToken);
        if (_audit is not null) await _audit.RecordAsync(null, "PROCESSING_STARTED", true, "ProcessingJob", job.Id, cancellationToken);
        var runs = await _db.AnalysisRuns.Where(x => x.MedicalReportId == report.Id).ToListAsync(cancellationToken);
        var run = runs.OrderByDescending(x => x.StartedAt).FirstOrDefault();
        if (run is null) { run = new AnalysisRun { MedicalReportId = report.Id, ProcessorVersion = job.ProcessingVersion }; _db.AnalysisRuns.Add(run); }
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _ai.AnalyseDocumentAsync(_storage.GetReportAbsolutePath(report.StoredFileName), report.OriginalFileName, cancellationToken);
            report.OcrRequired = response.RequiresOcr; report.OcrApplied = response.OcrApplied; report.ProcessingMode = response.ProcessingMode;
            report.PageSourcesJson = JsonSerializer.Serialize(response.PageSources); report.DocumentType = response.DocumentType;
            report.DocumentTypeConfidence = response.DocumentTypeConfidence; report.DocumentTypeSignalsJson = JsonSerializer.Serialize(response.DocumentTypeSignals ?? []);
            report.StructuredDataJson = JsonSerializer.Serialize(response.StructuredData ?? new Dictionary<string, object>());

            // A processing version plus a unique ReportId job makes retries idempotent. Existing rows are reused.
            var existing = await _db.LabResults.Where(x => x.MedicalReportId == report.Id).ToListAsync(cancellationToken);
            if (existing.Count == 0)
            {
                foreach (var extracted in response.Results)
                {
                    var entity = new LabResult
                    {
                        MedicalReportId = report.Id, OriginalTestName = extracted.OriginalName, NormalizedTestName = extracted.NormalizedName,
                        ValueNumeric = extracted.Value, ValueText = extracted.ValueText, Unit = extracted.Unit, OriginalUnit = extracted.OriginalUnit ?? extracted.Unit,
                        NormalizedUnit = extracted.NormalizedUnit ?? extracted.Unit, ValueOperator = extracted.ValueOperator, ReferenceMin = extracted.ReferenceMin,
                        ReferenceMax = extracted.ReferenceMax, ReferenceText = extracted.ReferenceText, ReferenceType = extracted.ReferenceType,
                        ReferenceOperator = extracted.ReferenceOperator, ReportedFlag = extracted.ReportedFlag, SectionName = extracted.Section,
                        MethodText = extracted.MethodText, ReviewRequired = extracted.ReviewRequired, AmbiguityReason = extracted.AmbiguityReason,
                        FlagDiscrepancy = extracted.Discrepancy, CalculatedStatus = _classifier.Classify(extracted.Value, extracted.ReferenceMin, extracted.ReferenceMax, extracted.ReferenceType),
                        ExtractionConfidence = extracted.Confidence, PageNumber = extracted.Page, BoundingBoxJson = extracted.BoundingBoxJson, CreatedAt = DateTimeOffset.UtcNow,
                        ValidationIssues = extracted.ValidationIssues?.Select(i => new ValidationIssue { Code = i.Code, Severity = i.Severity, FieldName = i.Field, Message = i.Message, RequiresReview = i.RequiresReview }).ToList() ?? []
                    };
                    _db.LabResults.Add(entity);
                }
            }
            var ocrUnavailable = response.RequiresOcr && !response.OcrApplied;
            report.Status = ocrUnavailable ? ReportStatus.RequiresOcr : response.Results.Any(x => x.ReviewRequired) ? ReportStatus.RequiresReview : ReportStatus.Completed;
            report.AnalysedAt = DateTimeOffset.UtcNow; run.Status = AnalysisStatus.Completed; run.CompletedAt = DateTimeOffset.UtcNow;
            job.Status = ocrUnavailable || response.Results.Any(x => x.ReviewRequired) ? ProcessingJobStatus.REVIEW_REQUIRED : ProcessingJobStatus.COMPLETED;
            job.CompletedAt = DateTimeOffset.UtcNow; job.HeartbeatAt = job.CompletedAt;
            job.NextRetryAt = null;
            job.LastErrorCode = ocrUnavailable ? "OCR_UNAVAILABLE" : null;
            job.LastErrorSafeMessage = ocrUnavailable ? "OCR was required but unavailable; verify the source report." : null;
            await _db.SaveChangesAsync(cancellationToken);
            if (_audit is not null) await _audit.RecordAsync(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()), "PROCESSING_COMPLETED", true, "ProcessingJob", job.Id, cancellationToken);
            _logger.LogInformation("Processing completed jobId={JobId} reportId={ReportId} attempt={Attempt} status={Status} durationMs={DurationMs}", job.Id, report.Id, job.AttemptCount, job.Status, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        { throw; }
        catch (Exception ex)
        {
            // Failed result inserts remain Added after a rolled-back save. Retrying
            // those entities while saving FAILED repeats the same SQL error and
            // strands the job in PROCESSING (Phase 14 parallel-column fixture).
            foreach (var entry in _db.ChangeTracker.Entries<ValidationIssue>().Where(x => x.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            foreach (var entry in _db.ChangeTracker.Entries<LabResult>().Where(x => x.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            run.Status = AnalysisStatus.Failed; run.CompletedAt = DateTimeOffset.UtcNow;
            var transient = ex is HttpRequestException || ex.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) || ex is IOException;
            job.LastErrorCode = ex.Message.Contains("PAGE_LIMIT_EXCEEDED", StringComparison.OrdinalIgnoreCase) ? "PAGE_LIMIT_EXCEEDED" : ex.Message.Contains("PDF_INVALID", StringComparison.OrdinalIgnoreCase) ? "PDF_INVALID" : ex.Message.Contains("OCR", StringComparison.OrdinalIgnoreCase) ? "OCR_UNAVAILABLE" : transient ? "AI_SERVICE_UNAVAILABLE" : "PARSER_FAILED";
            job.LastErrorSafeMessage = "The document could not be processed automatically.";
            if (job.LastErrorCode is "PDF_INVALID" or "PAGE_LIMIT_EXCEEDED")
            { job.Status = ProcessingJobStatus.FAILED; job.CompletedAt = DateTimeOffset.UtcNow; report.Status = ReportStatus.Failed; }
            else if (transient && job.AttemptCount < job.MaxAttempts)
            { job.Status = ProcessingJobStatus.RETRYING; job.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(_configuration?.GetValue<double>("Processing:RetryBaseSeconds") ?? 1, 0.1, 300) * Math.Pow(2, Math.Max(0, job.AttemptCount - 1))); }
            else { job.Status = ProcessingJobStatus.FAILED; job.CompletedAt = DateTimeOffset.UtcNow; report.Status = ReportStatus.Failed; }
            await _db.SaveChangesAsync(cancellationToken);
            if (_audit is not null) await _audit.RecordAsync(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()), job.Status == ProcessingJobStatus.RETRYING ? "PROCESSING_RETRY" : "PROCESSING_FAILED", false, "ProcessingJob", job.Id, cancellationToken);
            _logger.LogWarning(ex, "Processing failed jobId={JobId} reportId={ReportId} attempt={Attempt} errorCode={ErrorCode} durationMs={DurationMs}", job.Id, report.Id, job.AttemptCount, job.LastErrorCode, sw.ElapsedMilliseconds);
        }
    }
}
