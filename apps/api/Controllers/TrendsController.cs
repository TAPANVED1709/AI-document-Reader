using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using AI.DocumentReader.Api.Services;

namespace AI.DocumentReader.Api.Controllers;

[ApiController, Authorize]
[Route("api/trends")]
public class TrendsController : ControllerBase
{
    private readonly DocumentDbContext _db;
    private readonly IMedicalResourceAuthorizationService _authorization;
    public TrendsController(DocumentDbContext db, IMedicalResourceAuthorizationService authorization) { _db = db; _authorization = authorization; }

    public record CompareRequest(List<Guid> ReportIds, string? TestName = null, DateTimeOffset? FromDate = null, DateTimeOffset? ToDate = null);

    [HttpGet("tests")]
    public async Task<IActionResult> Tests(CancellationToken ct) => Ok(await _db.LabResults.AsNoTracking().Where(r => r.NormalizedTestName != null).Select(r => r.NormalizedTestName!).Distinct().OrderBy(x => x).ToListAsync(ct));

    [HttpGet("{normalizedTestName}")]
    public Task<IActionResult> Get(string normalizedTestName, CancellationToken ct) => Compare(new CompareRequest(new List<Guid>(), normalizedTestName), ct);

    [HttpPost("compare")]
    public async Task<IActionResult> Compare([FromBody] CompareRequest request, CancellationToken ct)
    {
        var userId = Guid.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var uid) ? uid : Guid.Empty;
        var organizationId = Guid.TryParse(User.FindFirstValue("organization_id"), out var oid) ? oid : Guid.Empty;
        var allowedReportIds = await _db.MedicalReports.AsNoTracking().Where(r => r.UploadedByUserId == userId || r.PatientUserId == userId || (r.OrganizationId == organizationId && organizationId != Guid.Empty)).Select(r => r.Id).ToListAsync(ct);
        var query = _db.LabResults.AsNoTracking().Include(r => r.MedicalReport).Where(r => allowedReportIds.Contains(r.MedicalReportId)).AsQueryable();
        if (request.ReportIds.Count > 0) query = query.Where(r => request.ReportIds.Contains(r.MedicalReportId));
        var all = await query.ToListAsync(ct);
        var selected = all.Where(r => string.IsNullOrWhiteSpace(request.TestName) || Canonical(r.CorrectedTestName ?? r.NormalizedTestName ?? r.OriginalTestName) == Canonical(request.TestName!)).Where(r => DateInRange(r.MedicalReport?.ReportDate ?? r.MedicalReport?.UploadedAt, request)).ToList();
        var points = selected.Select(ToPoint).ToList();
        var usable = points.Where(p => p.Included && p.Value.HasValue && p.Date.HasValue && p.ReviewState != "REVIEW_REQUIRED").ToList();
        var units = usable.Select(p => (p.Unit ?? "").Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToList();
        if (units.Count > 1) foreach (var p in usable) { p.Included = false; p.ExclusionReason = "UNIT_MISMATCH"; }
        var numeric = usable.Where(p => p.Included).OrderBy(p => p.Date).ToList();
        var first = numeric.FirstOrDefault()?.Value; var latest = numeric.LastOrDefault()?.Value; var change = first.HasValue && latest.HasValue ? latest - first : null;
        var isPercent = numeric.FirstOrDefault()?.Unit?.Trim() is "%" or "percent";
        return Ok(new { test = Canonical(request.TestName ?? selected.FirstOrDefault()?.NormalizedTestName ?? selected.FirstOrDefault()?.OriginalTestName ?? ""), unit = numeric.FirstOrDefault()?.Unit, points, requiresVerificationTrendPoints = points.Where(p => p.ReviewState == "REVIEW_REQUIRED").ToList(), validationIssues = points.Where(p => !p.Date.HasValue).Select(_ => "DATE_MISSING").Distinct().Concat(numeric.Count < 2 ? new[] { "INSUFFICIENT_POINTS" } : Array.Empty<string>()), firstValue = first, latestValue = latest, absoluteChange = change, percentagePointChange = isPercent ? change : null, percentChange = !isPercent && first.HasValue && first != 0 ? change / first * 100 : null, direction = change > 0 ? "INCREASED" : change < 0 ? "DECREASED" : change.HasValue ? "UNCHANGED" : "INSUFFICIENT_DATA" });
    }

    private static bool DateInRange(DateTimeOffset? value, CompareRequest request) => (!request.FromDate.HasValue || value >= request.FromDate) && (!request.ToDate.HasValue || value <= request.ToDate);
    private static string Canonical(string value) { var key = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant(); return key switch { "hb" or "hgb" or "haemoglobin" => "hemoglobin", "glycosylatedhemoglobin" => "hba1c", _ => key }; }
    private static TrendPoint ToPoint(LabResult r) => new(r.MedicalReportId, r.MedicalReport?.ReportDate ?? r.MedicalReport?.UploadedAt, r.MedicalReport?.ReportDateSource ?? "UPLOAD_DATE", r.CorrectedTestName ?? r.NormalizedTestName ?? r.OriginalTestName, r.CorrectedValueNumeric ?? r.ValueNumeric, r.CorrectedValueText ?? r.ValueText, r.CorrectedUnit ?? r.NormalizedUnit ?? r.Unit, r.OriginalUnit ?? r.Unit, r.ReferenceText, r.CorrectedReferenceMin ?? r.ReferenceMin, r.CorrectedReferenceMax ?? r.ReferenceMax, r.CalculatedStatus.ToString(), r.ReviewRequired ? "REVIEW_REQUIRED" : r.IsVerified ? "HUMAN_VERIFIED" : r.CorrectedAt.HasValue ? "HUMAN_CORRECTED" : "AUTO_ACCEPTED", !r.ReviewRequired, r.ReviewRequired ? "UNRESOLVED_REVIEW_POINT" : null);
    private sealed class TrendPoint(Guid reportId, DateTimeOffset? date, string dateSource, string test, decimal? value, string valueText, string? unit, string? originalUnit, string? reference, decimal? referenceMin, decimal? referenceMax, string status, string reviewState, bool included, string? exclusionReason)
    { public Guid ReportId { get; } = reportId; public DateTimeOffset? Date { get; } = date; public string DateSource { get; } = dateSource; public string Test { get; } = test; public decimal? Value { get; } = value; public string ValueText { get; } = valueText; public string? Unit { get; } = unit; public string? OriginalUnit { get; } = originalUnit; public string? Reference { get; } = reference; public decimal? ReferenceMin { get; } = referenceMin; public decimal? ReferenceMax { get; } = referenceMax; public string Status { get; } = status; public string ReviewState { get; } = reviewState; public bool Included { get; set; } = included; public string? ExclusionReason { get; set; } = exclusionReason; }
}
