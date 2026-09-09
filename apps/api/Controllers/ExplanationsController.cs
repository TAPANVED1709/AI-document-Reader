using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace AI.DocumentReader.Api.Controllers;

[ApiController, Authorize]
[Route("api")]
public class ExplanationsController : ControllerBase
{
    private readonly DocumentDbContext _db;
    private readonly IAiServiceClient _ai;
    private readonly IMedicalResourceAuthorizationService _authorization;
    private readonly ISecurityAuditService? _audit;
    private readonly OllamaConcurrencyLimiter _limiter;
    public ExplanationsController(DocumentDbContext db, IAiServiceClient ai, IMedicalResourceAuthorizationService authorization, OllamaConcurrencyLimiter limiter, ISecurityAuditService? audit = null) { _db = db; _ai = ai; _authorization = authorization; _limiter = limiter; _audit = audit; }

    [HttpGet("local-ai/health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken) => Ok(await _ai.LocalHealthAsync(cancellationToken));

    [HttpPost("reports/{reportId:guid}/explanations/patient")]
    [EnableRateLimiting("explanation")]
    public Task<IActionResult> Patient(Guid reportId, CancellationToken cancellationToken) => Generate(reportId, "PATIENT_SIMPLE", null, cancellationToken);

    [HttpPost("reports/{reportId:guid}/explanations/overview")]
    [EnableRateLimiting("explanation")]
    public Task<IActionResult> Overview(Guid reportId, CancellationToken cancellationToken) => Generate(reportId, "REPORT_OVERVIEW", null, cancellationToken);
    [HttpPost("reports/{reportId:guid}/explanations/clinician")]
    [EnableRateLimiting("explanation")]
    public Task<IActionResult> Clinician(Guid reportId, CancellationToken cancellationToken) => Generate(reportId, "CLINICIAN_SUMMARY", null, cancellationToken);

    [HttpPost("reports/{reportId:guid}/results/{resultId:guid}/explain")]
    [EnableRateLimiting("explanation")]
    public Task<IActionResult> Result(Guid reportId, Guid resultId, CancellationToken cancellationToken) => Generate(reportId, "RESULT_EXPLANATION", resultId, cancellationToken);

    private async Task<IActionResult> Generate(Guid reportId, string mode, Guid? resultId, CancellationToken cancellationToken)
    {
        if (!await _authorization.CanViewReportAsync(User, reportId, cancellationToken)) { if (_audit is not null) await _audit.RecordAsync(User, "ACCESS_DENIED", false, "MedicalReport", reportId, cancellationToken); return NotFound(new { error = "Report was not found." }); }
        var report = await _db.MedicalReports.Include(r => r.LabResults).ThenInclude(r => r.ValidationIssues).FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report is null) return NotFound(new { error = "Report was not found." });
        var selected = resultId.HasValue ? report.LabResults.Where(r => r.Id == resultId).ToList() : report.LabResults.ToList();
        if (resultId.HasValue && selected.Count == 0) return NotFound(new { error = "Result was not found." });
        var request = new AiExplanationRequestDto(mode, new { resultCount = selected.Count, reviewRequired = selected.Count(r => r.ReviewRequired) }, selected.Select(ToStructured).ToList());
        await _limiter.Gate.WaitAsync(cancellationToken);
        AiExplanationResponseDto response;
        try { response = await _ai.GenerateExplanationAsync(request, cancellationToken); }
        finally { _limiter.Gate.Release(); }
        var record = new ExplanationRecord { MedicalReportId = reportId, LabResultId = resultId, Mode = mode, Provider = response.Provider, Model = response.Model, PromptVersion = response.PromptVersion, GeneratedText = response.Summary, GeneratedJson = JsonSerializer.Serialize(response), CreatedAt = DateTimeOffset.UtcNow };
        _db.ExplanationRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        if (_audit is not null) await _audit.RecordAsync(User, "EXPLANATION_GENERATE", true, "MedicalReport", reportId, cancellationToken);
        return Ok(response);
    }

    private static AiStructuredResultDto ToStructured(LabResult r) => new(
        r.CorrectedTestName ?? r.NormalizedTestName ?? r.OriginalTestName,
        r.CorrectedValueNumeric ?? r.ValueNumeric,
        r.CorrectedValueText ?? r.ValueText,
        r.CorrectedUnit ?? r.Unit,
        r.CorrectedReferenceText ?? r.ReferenceText,
        r.CalculatedStatus.ToString(),
        r.ReportedFlag,
        r.IsVerified ? "HUMAN_VERIFIED" : r.CorrectedAt.HasValue ? "HUMAN_CORRECTED" : r.ReviewRequired ? "REVIEW_REQUIRED" : "AUTO_ACCEPTED",
        r.ReviewRequired,
        r.ValidationIssues.Select(i => new AiValidationIssueDto(i.Code, i.Severity, i.FieldName, i.Message, i.RequiresReview)).ToList(),
        r.SectionName);
}

