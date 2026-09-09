using System.Security.Claims;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Controllers;

[ApiController, Authorize(Roles = "LAB_STAFF,PATHOLOGIST")]
public sealed class ProcessingController : ControllerBase
{
    private readonly DocumentDbContext _db; private readonly ILocalStorageService _storage; private readonly IMedicalResourceAuthorizationService _auth; private readonly ISecurityAuditService? _audit;
    public ProcessingController(DocumentDbContext db, ILocalStorageService storage, IMedicalResourceAuthorizationService auth, ISecurityAuditService? audit = null) { _db = db; _storage = storage; _auth = auth; _audit = audit; }

    [HttpGet("api/reports/{id:guid}/processing-status")]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct)
    {
        if (!await _auth.CanViewReportAsync(User, id, ct)) return NotFound(new { error = "Medical report was not found." });
        var job = await _db.ProcessingJobs.AsNoTracking().FirstOrDefaultAsync(x => x.ReportId == id, ct);
        if (job is null) return NotFound(new { error = "Processing job was not found." });
        return Ok(new { reportId = id, jobId = job.Id, status = job.Status.ToString(), attemptCount = job.AttemptCount, createdAt = job.CreatedAt, startedAt = job.StartedAt, completedAt = job.CompletedAt, safeErrorCode = job.LastErrorCode });
    }

    [HttpGet("api/queue/health")]
    public async Task<IActionResult> QueueHealth(CancellationToken ct)
    {
        var counts = await _db.ProcessingJobs.AsNoTracking().GroupBy(x => x.Status).Select(g => new { status = g.Key.ToString(), count = g.Count() }).ToListAsync(ct);
        return Ok(new { queued = counts.FirstOrDefault(x => x.status == "QUEUED")?.count ?? 0, processing = counts.FirstOrDefault(x => x.status == "PROCESSING")?.count ?? 0, retrying = counts.FirstOrDefault(x => x.status == "RETRYING")?.count ?? 0, failed = counts.FirstOrDefault(x => x.status == "FAILED")?.count ?? 0 });
    }

    [HttpGet("api/review-queue")]
    public async Task<IActionResult> ReviewQueue([FromQuery] string? search, [FromQuery] string? severity, CancellationToken ct)
    {
        var org = ClaimId("organization_id");
        var query = _db.MedicalReports.AsNoTracking().Include(r => r.LabResults).ThenInclude(x => x.ValidationIssues).Where(r => r.LabResults.Any(x => x.ReviewRequired));
        if (org.HasValue) query = query.Where(r => r.OrganizationId == org);
        var rows = await query.OrderByDescending(r => r.UploadedAt).Take(200).ToListAsync(ct);
        var result = rows.Select(r => new { reportId = r.Id, fileName = r.OriginalFileName, uploadedAt = r.UploadedAt, reportDate = r.ReportDate, processingMode = r.ProcessingMode, status = r.Status.ToString(), issueCount = r.LabResults.Count(x => x.ReviewRequired), highestExtractionSeverity = r.LabResults.Any(x => x.ValidationIssues.Any(i => i.Severity == "HIGH")) ? "HIGH" : r.LabResults.Any(x => x.ValidationIssues.Any()) ? "WARNING" : "INFO", panel = r.LabResults.Select(x => x.SectionName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Other" }).Where(x => string.IsNullOrWhiteSpace(search) || x.fileName.Contains(search, StringComparison.OrdinalIgnoreCase));
        return Ok(result);
    }

    [HttpPost("api/reports/batch")]
    [EnableRateLimiting("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Batch([FromForm] List<IFormFile> files, CancellationToken ct)
    {
        var output = new List<object>();
        foreach (var file in files ?? [])
        {
            try { _storage.ValidateUploadFile(file); var item = await CreateQueuedReport(file, null, ct); output.Add(new { file = file.FileName, item.reportId, item.jobId, status = "QUEUED" }); }
            catch (ArgumentException ex) { output.Add(new { file = file.FileName, status = ex.Message.Contains("PDF", StringComparison.OrdinalIgnoreCase) ? "INVALID_PDF" : "INVALID_FILE" }); }
        }
        if (_audit is not null) await _audit.RecordAsync(User, "BATCH_UPLOAD", true, "Batch", null, ct);
        return Accepted(output);
    }

    [HttpPost("api/lab-ingestion/reports")]
    [EnableRateLimiting("ingestion")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Ingest([FromForm] IFormFile file, [FromForm] Guid patientId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var org = ClaimId("organization_id");
        if (!org.HasValue || patientId == Guid.Empty) return BadRequest(new { error = "Organization and authorized patient association are required." });
        if (!await PatientAuthorized(patientId, org.Value, ct)) return NotFound(new { error = "Patient was not found or is not authorized for this organization." });
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200) return BadRequest(new { error = "A valid Idempotency-Key is required." });
        var existing = await _db.IngestionIdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x => x.OrganizationId == org && x.Key == idempotencyKey, ct);
        if (existing is not null) return Accepted(new { reportId = existing.ReportId, jobId = existing.JobId, status = "QUEUED", idempotent = true });
        try { _storage.ValidateUploadFile(file); var item = await CreateQueuedReport(file, patientId, ct); _db.IngestionIdempotencyRecords.Add(new IngestionIdempotencyRecord { OrganizationId = org.Value, Key = idempotencyKey, ReportId = item.reportId, JobId = item.jobId }); await _db.SaveChangesAsync(ct); if (_audit is not null) await _audit.RecordAsync(User, "LAB_INGESTION", true, "MedicalReport", item.reportId, ct); return Accepted(new { item.reportId, item.jobId, status = "QUEUED" }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private async Task<(Guid reportId, Guid jobId)> CreateQueuedReport(IFormFile file, Guid? patientId, CancellationToken ct)
    {
        var stored = await _storage.SaveReportAsync(file, ct); var report = new MedicalReport { OriginalFileName = file.FileName, StoredFileName = stored.storedFileName, ContentType = file.ContentType, FileSize = file.Length, Status = ReportStatus.Processing, UploadedAt = DateTimeOffset.UtcNow, PatientUserId = patientId, UploadedByUserId = ClaimId(ClaimTypes.NameIdentifier), OrganizationId = ClaimId("organization_id") };
        var job = new ProcessingJob { ReportId = report.Id, Status = ProcessingJobStatus.QUEUED, MaxAttempts = 3 }; _db.MedicalReports.Add(report); _db.ProcessingJobs.Add(job); await _db.SaveChangesAsync(ct); if (_audit is not null) await _audit.RecordAsync(User, "REPORT_QUEUED", true, "MedicalReport", report.Id, ct); return (report.Id, job.Id);
    }
    private async Task<bool> PatientAuthorized(Guid patientId, Guid org, CancellationToken ct) => ClaimId(ClaimTypes.NameIdentifier) == patientId || await _db.PatientAccessGrants.AnyAsync(x => x.PatientId == patientId && x.GrantedToOrganizationId == org && x.RevokedAt == null && (!x.ExpiresAt.HasValue || x.ExpiresAt > DateTimeOffset.UtcNow), ct);
    private Guid? ClaimId(string type) => Guid.TryParse(User.FindFirstValue(type), out var id) ? id : null;
}
