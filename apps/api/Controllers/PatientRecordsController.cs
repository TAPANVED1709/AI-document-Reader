using System.Security.Claims;
using System.Text.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Controllers;

[ApiController, Authorize(Roles = "PATIENT")]
[Route("api/patient")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PatientRecordsController(DocumentDbContext db, ISecurityAuditService audit) : ControllerBase
{
    private Guid PatientId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private IQueryable<MedicalReport> Owned => db.MedicalReports.AsNoTracking().Where(r => r.PatientUserId == PatientId);
    private Task Audit(string action, Guid? id, CancellationToken ct) => audit.RecordAsync(User, action, true, "MedicalReport", id, ct);
    private static JsonElement Data(string? json) => JsonSerializer.Deserialize<JsonElement>(json ?? "{}");
    private static string? Header(string? json, string key)
    {
        var root = Data(json);
        if (root.TryGetProperty("report", out var header) && header.ValueKind == JsonValueKind.Object) root = header;
        return root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
    private static object Nullable(string? value) => JsonSerializer.SerializeToElement(value);
    private static string State(ReportStatus status) => status switch
    {
        ReportStatus.Completed => "COMPLETED", ReportStatus.RequiresReview or ReportStatus.RequiresOcr => "NEEDS_REVIEW",
        ReportStatus.Failed => "FAILED", _ => "PROCESSING"
    };

    [HttpGet("medical-records")]
    public Task<IActionResult> Records(int page = 1, int pageSize = 20, string? filter = null, string? search = null, CancellationToken ct = default)
        => List(false, page, pageSize, filter, search, ct);

    [HttpGet("medical-timeline")]
    public Task<IActionResult> Timeline(int page = 1, int pageSize = 20, CancellationToken ct = default)
        => List(true, page, pageSize, null, null, ct);

    private async Task<IActionResult> List(bool timeline, int page, int pageSize, string? filter, string? search, CancellationToken ct)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100 || page > 100000 || search?.Length > 200)
            return BadRequest(new { error = "Use a valid page and page size between 1 and 100." });
        if (filter is not (null or "ALL" or "COMPLETED" or "NEEDS_REVIEW")) return BadRequest(new { error = "Unknown record filter." });
        // One owner-scoped SQL projection, with correlated aggregate counts; no PDFs or result collections.
        // Legacy explicit dates live in JSON. Sort those lightweight headers before paging rather than use a fabricated fallback.
        var headers = await Owned.Select(r => new
        {
            r.Id, r.OriginalFileName, r.StructuredDataJson, r.UploadedAt, r.DocumentType, r.ProcessingMode, r.Status,
            TestCount = r.LabResults.Count,
            HighCount = r.LabResults.Count(l => l.CalculatedStatus == ResultStatus.HIGH),
            LowCount = r.LabResults.Count(l => l.CalculatedStatus == ResultStatus.LOW),
            NormalCount = r.LabResults.Count(l => l.CalculatedStatus == ResultStatus.NORMAL),
            NeedsReviewCount = r.LabResults.Count(l => l.ReviewRequired || l.ApplicabilityStatus == "REVIEW_REQUIRED" || l.ValidationIssues.Any(i => i.RequiresReview && !i.IsResolved))
        }).ToListAsync(ct);
        var rows = headers.Select(r => new
        {
            reportId = r.Id, originalFileName = r.OriginalFileName, laboratoryName = Header(r.StructuredDataJson, "laboratoryName"),
            reportNumber = Header(r.StructuredDataJson, "reportNumber"), reportGeneratedDate = MedicalReportSummaryMapper.ReportGeneratedDate(r.StructuredDataJson),
            uploadedAt = r.UploadedAt, documentType = r.DocumentType, processingMode = r.ProcessingMode, status = State(r.Status),
            reviewRequired = r.NeedsReviewCount > 0 || r.Status == ReportStatus.RequiresReview || r.Status == ReportStatus.RequiresOcr,
            testCount = r.TestCount, highCount = r.HighCount, lowCount = r.LowCount, normalCount = r.NormalCount, needsReviewCount = r.NeedsReviewCount
        }).Where(r => filter is null or "ALL" || (filter == "NEEDS_REVIEW" ? r.reviewRequired : r.status == "COMPLETED" && !r.reviewRequired))
        .Where(r => string.IsNullOrWhiteSpace(search) || new[] { r.laboratoryName, r.originalFileName, r.reportNumber }.Any(s => s?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true));
        var ordered = timeline ? rows.OrderBy(r => r.reportGeneratedDate == null).ThenBy(r => r.reportGeneratedDate).ThenBy(r => r.uploadedAt).ThenBy(r => r.reportId)
            : rows.OrderBy(r => r.reportGeneratedDate == null).ThenByDescending(r => r.reportGeneratedDate).ThenByDescending(r => r.uploadedAt).ThenBy(r => r.reportId);
        await Audit(timeline ? "MEDICAL_TIMELINE_VIEW" : "MEDICAL_RECORD_LIST", null, ct);
        // Emit null explicitly despite the application's global null suppression.
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(r => new
        {
            r.reportId, r.originalFileName, laboratoryName = Nullable(r.laboratoryName), reportNumber = Nullable(r.reportNumber),
            reportGeneratedDate = Nullable(r.reportGeneratedDate), r.uploadedAt, r.documentType, r.processingMode, r.status,
            r.reviewRequired, r.testCount, r.highCount, r.lowCount, r.normalCount, r.needsReviewCount
        }).ToList();
        return Ok(new { items, total = ordered.Count(), page, pageSize });
    }

    [HttpGet("medical-records/{reportId:guid}")]
    public async Task<IActionResult> Detail(Guid reportId, CancellationToken ct)
    {
        var report = await Owned.Include(r => r.LabResults).ThenInclude(l => l.ValidationIssues).SingleOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null) return NotFound(new { error = "Medical report was not found." });
        await Audit("MEDICAL_RECORD_VIEW", reportId, ct);
        var data = Data(report.StructuredDataJson);
        return Ok(new
        {
            id = report.Id, reportId = report.Id, report.OriginalFileName, report.UploadedAt,
            reportGeneratedDate = Nullable(MedicalReportSummaryMapper.ReportGeneratedDate(report.StructuredDataJson)),
            status = State(report.Status), report.ProcessingMode, report.OcrRequired, report.OcrApplied, report.DocumentType,
            patient = data.TryGetProperty("patient", out var patient) ? patient : JsonSerializer.SerializeToElement(new { }),
            report = data.TryGetProperty("report", out var header) ? header : JsonSerializer.SerializeToElement(new { }),
            structuredData = data, resultsCount = report.LabResults.Count,
            results = report.LabResults.OrderBy(r => r.PageNumber).ThenBy(r => r.CreatedAt).Select(r => ReportsController.MapResultToDto(r, report))
        });
    }

    [HttpGet("test-history/{normalizedTestName}")]
    public Task<IActionResult> History(string normalizedTestName, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => Measurements(normalizedTestName, false, page, pageSize, ct);

    [HttpGet("latest-results")]
    public Task<IActionResult> Latest(int page = 1, int pageSize = 20, CancellationToken ct = default)
        => Measurements(null, true, page, pageSize, ct);

    private async Task<IActionResult> Measurements(string? name, bool latest, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100 || name?.Length > 255) return BadRequest(new { error = "Invalid history request." });
        var query = db.LabResults.AsNoTracking().Where(l => l.MedicalReport!.PatientUserId == PatientId);
        // Use persisted normalization. No re-extraction, clinical inference, or unit conversion.
        if (name is not null) query = query.Where(l => (l.CorrectedTestName ?? l.NormalizedTestName ?? l.OriginalTestName).ToUpper() == name.ToUpper());
        var rows = await query.Select(l => new LabResult
        {
            Id = l.Id, MedicalReportId = l.MedicalReportId, OriginalTestName = l.OriginalTestName, NormalizedTestName = l.NormalizedTestName,
            CorrectedTestName = l.CorrectedTestName, ValueNumeric = l.ValueNumeric, CorrectedValueNumeric = l.CorrectedValueNumeric,
            Unit = l.Unit, CorrectedUnit = l.CorrectedUnit, ReferenceText = l.ReferenceText, CorrectedReferenceText = l.CorrectedReferenceText,
            IsVerified = l.IsVerified, ReviewRequired = l.ReviewRequired, ApplicabilityStatus = l.ApplicabilityStatus,
            DemographicQualifier = l.DemographicQualifier, ValueOperator = l.ValueOperator, CalculatedStatus = l.CalculatedStatus,
            MedicalReport = new MedicalReport { Id = l.MedicalReportId, UploadedAt = l.MedicalReport!.UploadedAt, StructuredDataJson = l.MedicalReport.StructuredDataJson }
        }).ToListAsync(ct);
        var conflicts = LabResultTrust.ConflictingDemographicRows(rows);
        var trusted = rows.Where(l => LabResultTrust.IsTrusted(l) && !conflicts.Contains(l.Id) && (string.IsNullOrEmpty(l.ValueOperator) || l.ValueOperator == "="));
        var points = trusted.Select(l => new
        {
            id = l.Id, reportId = l.MedicalReportId, testName = l.CorrectedTestName ?? l.NormalizedTestName ?? l.OriginalTestName,
            originalTestName = l.OriginalTestName, reportDate = MedicalReportSummaryMapper.ReportGeneratedDate(l.MedicalReport!.StructuredDataJson),
            uploadedAt = l.MedicalReport.UploadedAt, laboratoryName = Header(l.MedicalReport.StructuredDataJson, "laboratoryName"),
            value = l.CorrectedValueNumeric ?? l.ValueNumeric, unit = l.CorrectedUnit ?? l.Unit,
            referenceRange = l.CorrectedReferenceText ?? l.ReferenceText, status = l.CalculatedStatus.ToString()
        }).ToList();
        if (latest) points = points.GroupBy(p => (p.testName, p.unit)).Select(g => g.OrderByDescending(p => p.reportDate).ThenByDescending(p => p.uploadedAt).First()).ToList();
        var ordered = latest ? points.OrderByDescending(p => p.reportDate).ThenByDescending(p => p.uploadedAt).ThenBy(p => p.id)
            : points.OrderBy(p => p.reportDate == null).ThenBy(p => p.reportDate).ThenBy(p => p.uploadedAt).ThenBy(p => p.id);
        await Audit(latest ? "LATEST_RESULTS_VIEW" : "TEST_HISTORY_VIEW", null, ct);
        return Ok(new { items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(p => new
        {
            p.id, p.reportId, p.testName, p.originalTestName, reportDate = Nullable(p.reportDate), p.uploadedAt,
            laboratoryName = Nullable(p.laboratoryName), p.value, unit = Nullable(p.unit), referenceRange = Nullable(p.referenceRange), p.status
        }), total = points.Count, page, pageSize, excludedCount = rows.Count - trusted.Count() });
    }
}
