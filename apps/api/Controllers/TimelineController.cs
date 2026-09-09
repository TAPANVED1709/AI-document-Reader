using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using AI.DocumentReader.Api.Services;

namespace AI.DocumentReader.Api.Controllers;

[ApiController, Authorize]
[Route("api/timeline")]
public class TimelineController : ControllerBase
{
    private readonly DocumentDbContext _db;
    private readonly IMedicalResourceAuthorizationService _authorization;
    public TimelineController(DocumentDbContext db, IMedicalResourceAuthorizationService authorization) { _db = db; _authorization = authorization; }

    [HttpGet]
    public async Task<IActionResult> Timeline(int? year, int? month, string? section, string? testName, string? reviewState, CancellationToken ct)
    {
        var userId = Guid.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var uid) ? uid : Guid.Empty;
        var organizationId = Guid.TryParse(User.FindFirstValue("organization_id"), out var oid) ? oid : Guid.Empty;
        var reports = await _db.MedicalReports.AsNoTracking().Include(r => r.LabResults).Where(r => (r.UploadedByUserId == userId || r.PatientUserId == userId || r.OrganizationId == organizationId && organizationId != Guid.Empty) && (!year.HasValue || (r.ReportDate ?? r.UploadedAt).Year == year) && (!month.HasValue || (r.ReportDate ?? r.UploadedAt).Month == month)).ToListAsync(ct);
        var events = reports.Where(r => section is null && testName is null && reviewState is null || r.LabResults.Any(x => Matches(x, section, testName, reviewState))).Select(r => new { Report = r, Date = r.ReportDate ?? r.UploadedAt }).OrderByDescending(x => x.Date).Select(x => Event(x.Report, section, testName, reviewState)).ToList();
        return Ok(events);
    }

    [HttpGet("{reportId:guid}")]
    public async Task<IActionResult> Detail(Guid reportId, CancellationToken ct)
    {
        if (!await _authorization.CanViewReportAsync(User, reportId, ct)) return NotFound(new { error = "Report was not found." });
        var report = await _db.MedicalReports.AsNoTracking().Include(r => r.LabResults).ThenInclude(r => r.ValidationIssues).FirstOrDefaultAsync(r => r.Id == reportId, ct);
        return report is null ? NotFound(new { error = "Report was not found." }) : Ok(new { report = Event(report, null, null, null), results = report.LabResults.OrderBy(r => r.PageNumber).Select(Result) });
    }

    [HttpGet("latest-results")]
    public async Task<IActionResult> Latest(CancellationToken ct)
    {
        var results = await _db.LabResults.AsNoTracking().Include(r => r.MedicalReport).ToListAsync(ct);
        return Ok(results.Where(r => !r.ReviewRequired && r.ValueNumeric.HasValue).GroupBy(r => Canonical(r.CorrectedTestName ?? r.NormalizedTestName ?? r.OriginalTestName)).Select(g => g.OrderByDescending(r => r.MedicalReport!.ReportDate ?? r.MedicalReport.UploadedAt).First()).Select(Result).ToList());
    }

    [HttpGet("tests/{normalizedTestName}")]
    public async Task<IActionResult> History(string normalizedTestName, CancellationToken ct)
    {
        var results = (await _db.LabResults.AsNoTracking().Include(r => r.MedicalReport).ToListAsync(ct)).Where(r => Canonical(r.CorrectedTestName ?? r.NormalizedTestName ?? r.OriginalTestName) == Canonical(normalizedTestName)).OrderByDescending(r => r.MedicalReport!.ReportDate ?? r.MedicalReport.UploadedAt).ToList();
        return Ok(results.Select(Result));
    }

    private static object Event(MedicalReport r, string? section, string? testName, string? reviewState)
    {
        var results = r.LabResults.Where(x => (section is null || x.SectionName == section) && (testName is null || Canonical(x.NormalizedTestName ?? x.OriginalTestName) == Canonical(testName)) && (reviewState is null || State(x) == reviewState)).ToList();
        return new { eventId = r.Id, reportId = r.Id, date = r.ReportDate ?? r.UploadedAt, dateSource = r.ReportDateSource, title = Title(r, results), reportType = r.DocumentType, documentType = r.DocumentType, sections = results.Select(x => x.SectionName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(), resultCount = results.Count, highCount = results.Count(x => x.CalculatedStatus == ResultStatus.HIGH), lowCount = results.Count(x => x.CalculatedStatus == ResultStatus.LOW), normalCount = results.Count(x => x.CalculatedStatus == ResultStatus.NORMAL), unknownCount = results.Count(x => x.CalculatedStatus == ResultStatus.UNKNOWN), reviewRequiredCount = results.Count(x => x.ReviewRequired), verifiedCount = results.Count(x => x.IsVerified), correctedCount = results.Count(x => x.CorrectedAt.HasValue) };
    }
    private static bool Matches(LabResult x, string? section, string? testName, string? reviewState) => (section is null || x.SectionName == section) && (testName is null || Canonical(x.NormalizedTestName ?? x.OriginalTestName) == Canonical(testName)) && (reviewState is null || State(x) == reviewState);
    private static object Result(LabResult r) => new { id = r.Id, reportId = r.MedicalReportId, test = r.CorrectedTestName ?? r.NormalizedTestName ?? r.OriginalTestName, value = r.CorrectedValueNumeric ?? r.ValueNumeric, valueText = r.CorrectedValueText ?? r.ValueText, unit = r.CorrectedUnit ?? r.NormalizedUnit ?? r.Unit, date = r.MedicalReport?.ReportDate ?? r.MedicalReport?.UploadedAt, dateSource = r.MedicalReport?.ReportDateSource ?? "UPLOAD_DATE", status = r.CalculatedStatus.ToString(), reviewState = State(r), included = !r.ReviewRequired, originalValue = r.ValueNumeric, correctedValue = r.CorrectedValueNumeric, correctedAt = r.CorrectedAt, correctionReason = r.CorrectionReason, confidence = r.ExtractionConfidence, section = r.SectionName };
    private static string State(LabResult r) => r.ReviewRequired ? "REVIEW_REQUIRED" : r.CorrectedAt.HasValue ? "HUMAN_CORRECTED" : r.IsVerified ? "HUMAN_VERIFIED" : "AUTO_ACCEPTED";
    private static string Title(MedicalReport r, List<LabResult> results) => results.Select(x => x.SectionName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? r.DocumentType switch { "DISCHARGE_SUMMARY" => "Discharge Summary", "PRESCRIPTION" => "Prescription", "RADIOLOGY_REPORT" => "Radiology Report", _ => r.OriginalFileName.Contains("cbc", StringComparison.OrdinalIgnoreCase) ? "Complete Blood Count" : r.OriginalFileName.Contains("liver", StringComparison.OrdinalIgnoreCase) ? "Liver Function Test" : r.OriginalFileName.Contains("thyroid", StringComparison.OrdinalIgnoreCase) ? "Thyroid Profile" : "Laboratory Report" };
    private static string Canonical(string value) { var key = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant(); return key switch { "hb" or "hgb" or "haemoglobin" => "hemoglobin", "glycosylatedhemoglobin" => "hba1c", _ => key }; }
}
