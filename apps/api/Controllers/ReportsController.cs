using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace AI.DocumentReader.Api.Controllers;

[ApiController, Authorize]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    public record ResultCorrectionRequest(
        [MaxLength(255)] string? TestName,
        decimal? Value,
        [MaxLength(100)] string? ValueText,
        [MaxLength(50)] string? Unit,
        decimal? ReferenceMin,
        decimal? ReferenceMax,
        [MaxLength(100)] string? ReferenceText,
        [MaxLength(1000)] string? Reason,
        [MaxLength(255)] string? CorrectedBy);

    private readonly DocumentDbContext _dbContext;
    private readonly ILocalStorageService _storageService;
    private readonly IAiServiceClient _aiServiceClient;
    private readonly IReferenceRangeClassifier _rangeClassifier;
    private readonly ILogger<ReportsController> _logger;
    private readonly IMedicalResourceAuthorizationService _authorization;
    private readonly bool _enforceAuthorization;
    private readonly ISecurityAuditService? _audit;

    public ReportsController(
        DocumentDbContext dbContext,
        ILocalStorageService storageService,
        IAiServiceClient aiServiceClient,
        IReferenceRangeClassifier rangeClassifier,
        ILogger<ReportsController> logger, IMedicalResourceAuthorizationService? authorization = null, ISecurityAuditService? audit = null)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _aiServiceClient = aiServiceClient;
        _rangeClassifier = rangeClassifier;
        _logger = logger;
        _enforceAuthorization = authorization is not null;
        _authorization = authorization ?? new MedicalResourceAuthorizationService(dbContext);
        _audit = audit;
    }

    /// <summary>
    /// Uploads a PDF medical laboratory report and executes document intelligence analysis.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Roles = "LAB_STAFF,PATHOLOGIST")]
    [EnableRateLimiting("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20 MB limit
    public async Task<IActionResult> UploadReport(IFormFile file, CancellationToken cancellationToken)
    {
        try
        {
            _storageService.ValidateUploadFile(file);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("File upload validation failed: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }

        // 1. Save document to local storage
        string storedFileName;
        string absolutePath;
        try
        {
            (storedFileName, absolutePath) = await _storageService.SaveReportAsync(file, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save uploaded file to local storage.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to store uploaded document." });
        }

        // 2. Create initial MedicalReport record
        var report = new MedicalReport
        {
            OriginalFileName = file.FileName,
            StoredFileName = storedFileName,
            ContentType = file.ContentType,
            FileSize = file.Length,
            Status = ReportStatus.Processing,
            UploadedAt = DateTimeOffset.UtcNow
            ,UploadedByUserId = Guid.TryParse((User ?? new ClaimsPrincipal()).FindFirstValue(ClaimTypes.NameIdentifier), out var uploader) ? uploader : null
            ,OrganizationId = Guid.TryParse((User ?? new ClaimsPrincipal()).FindFirstValue("organization_id"), out var organization) ? organization : null
        };

        var analysisRun = new AnalysisRun
        {
            MedicalReportId = report.Id,
            StartedAt = DateTimeOffset.UtcNow,
            Status = AnalysisStatus.Running,
            ProcessorVersion = "1.0.0"
        };

        _dbContext.MedicalReports.Add(report);
        _dbContext.AnalysisRuns.Add(analysisRun);
        var processingJob = new ProcessingJob { ReportId = report.Id, Status = ProcessingJobStatus.QUEUED, MaxAttempts = 3, ProcessingVersion = "13B.1" };
        _dbContext.ProcessingJobs.Add(processingJob);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await Audit("REPORT_QUEUED", "MedicalReport", report.Id, true, cancellationToken);
        _logger.LogInformation("Report queued jobId={JobId} reportId={ReportId} status={Status}", processingJob.Id, report.Id, processingJob.Status);
        return Accepted(new { reportId = report.Id, jobId = processingJob.Id, status = processingJob.Status.ToString() });
    }

    /// <summary>
    /// Retrieves a medical report's metadata and processing status.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetReportById(Guid id, CancellationToken cancellationToken)
    {
        if (_enforceAuthorization && !await _authorization.CanViewReportAsync(User, id, cancellationToken)) { await Audit("ACCESS_DENIED", "MedicalReport", id, false, cancellationToken); return NotFound(new { error = "Medical report was not found." }); }
        var report = await _dbContext.MedicalReports
            .Include(r => r.LabResults).ThenInclude(l => l.ValidationIssues)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (report == null)
        {
            return NotFound(new { error = $"Medical report '{id}' was not found." });
        }

        await Audit("REPORT_VIEW", "MedicalReport", id, true, cancellationToken); return Ok(MapToDto(report, report.LabResults.ToList()));
    }

    /// <summary>
    /// Retrieves only the extracted lab results for a report.
    /// </summary>
    [HttpGet("{id:guid}/results")]
    public async Task<IActionResult> GetReportResults(Guid id, CancellationToken cancellationToken)
    {
        if (_enforceAuthorization && !await _authorization.CanViewReportAsync(User, id, cancellationToken)) { await Audit("ACCESS_DENIED", "MedicalReport", id, false, cancellationToken); return NotFound(new { error = "Medical report was not found." }); }
        var report = await _dbContext.MedicalReports.Include(r => r.LabResults).ThenInclude(l => l.ValidationIssues).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (report == null)
        {
            return NotFound(new { error = $"Medical report '{id}' was not found." });
        }

        return Ok(report.LabResults.OrderBy(l => l.PageNumber).ThenBy(l => l.OriginalTestName)
            .Select(r => MapResultToDto(r, report)).ToList());
    }

    [HttpGet("{id:guid}/file")]
    public IActionResult GetReportFile(Guid id)
    {
        if (_enforceAuthorization && !_authorization.CanViewReportAsync(User, id).GetAwaiter().GetResult()) { Audit("ACCESS_DENIED", "PDF", id, false, default).GetAwaiter().GetResult(); return NotFound(new { error = "Report file was not found." }); }
        var report = _dbContext.MedicalReports.AsNoTracking().FirstOrDefault(r => r.Id == id);
        if (report == null || !_storageService.FileExists(report.StoredFileName)) return NotFound(new { error = "Report file was not found." });
        Audit("PDF_VIEW", "PDF", id, true, default).GetAwaiter().GetResult(); return PhysicalFile(_storageService.GetReportAbsolutePath(report.StoredFileName), "application/pdf", report.OriginalFileName, enableRangeProcessing: true);
    }

    [HttpPost("{reportId:guid}/results/{resultId:guid}/verify")]
    public async Task<IActionResult> VerifyResult(Guid reportId, Guid resultId, CancellationToken cancellationToken)
    {
        if (_enforceAuthorization && !await _authorization.CanModifyReportAsync(User, reportId, cancellationToken)) { await Audit("ACCESS_DENIED", "LabResult", resultId, false, cancellationToken); return NotFound(new { error = "Result was not found." }); }
        var result = await _dbContext.LabResults.FirstOrDefaultAsync(r => r.Id == resultId && r.MedicalReportId == reportId, cancellationToken);
        if (result == null) return NotFound(new { error = "Result was not found." });
        result.IsVerified = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await Audit("RESULT_VERIFY", "LabResult", resultId, true, cancellationToken);
        return Ok(new { isVerified = true });
    }

    [HttpPatch("{reportId:guid}/results/{resultId:guid}")]
    public async Task<IActionResult> CorrectResult(Guid reportId, Guid resultId, [FromBody] ResultCorrectionRequest request, CancellationToken cancellationToken)
    {
        if (_enforceAuthorization && !await _authorization.CanModifyReportAsync(User, reportId, cancellationToken)) { await Audit("ACCESS_DENIED", "LabResult", resultId, false, cancellationToken); return NotFound(new { error = "Result was not found." }); }
        if (request.ReferenceMin.HasValue && request.ReferenceMax.HasValue && request.ReferenceMin > request.ReferenceMax)
            return BadRequest(new { error = "Reference minimum cannot exceed reference maximum." });
        if (request.TestName is null && request.Value is null && request.ValueText is null && request.Unit is null &&
            request.ReferenceMin is null && request.ReferenceMax is null && request.ReferenceText is null)
            return BadRequest(new { error = "At least one corrected value is required." });

        var result = await _dbContext.LabResults.FirstOrDefaultAsync(r => r.Id == resultId && r.MedicalReportId == reportId, cancellationToken);
        var report = await _dbContext.MedicalReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (result == null || report == null) return NotFound(new { error = "Result was not found." });
        var now = DateTimeOffset.UtcNow;
        var audit = new List<ResultCorrectionAudit>();
        void Change(string field, string? previous, string? next, Action apply)
        {
            if (next is null || next == previous) return;
            apply();
            audit.Add(new ResultCorrectionAudit { LabResultId = result.Id, FieldName = field, PreviousValue = previous, NewValue = next, Reason = request.Reason, ChangedBy = request.CorrectedBy, ChangedAt = now });
        }
        Change("TestName", result.CorrectedTestName ?? result.OriginalTestName, request.TestName, () => result.CorrectedTestName = request.TestName);
        Change("Value", (result.CorrectedValueNumeric ?? result.ValueNumeric)?.ToString(), request.Value?.ToString(), () => { result.CorrectedValueNumeric = request.Value; result.CorrectedValueText = request.ValueText ?? request.Value?.ToString(); });
        Change("Unit", result.CorrectedUnit ?? result.Unit, request.Unit, () => result.CorrectedUnit = request.Unit);
        Change("ReferenceMin", (result.CorrectedReferenceMin ?? result.ReferenceMin)?.ToString(), request.ReferenceMin?.ToString(), () => result.CorrectedReferenceMin = request.ReferenceMin);
        Change("ReferenceMax", (result.CorrectedReferenceMax ?? result.ReferenceMax)?.ToString(), request.ReferenceMax?.ToString(), () => result.CorrectedReferenceMax = request.ReferenceMax);
        Change("ReferenceText", result.CorrectedReferenceText ?? result.ReferenceText, request.ReferenceText, () => result.CorrectedReferenceText = request.ReferenceText);
        if (request.ValueText is not null) result.CorrectedValueText = request.ValueText;
        if (audit.Count == 0) return BadRequest(new { error = "Corrections did not change any value." });
        result.CorrectionReason = request.Reason;
        result.CorrectedAt = now;
        result.CorrectedBy = request.CorrectedBy;
        foreach (var issue in await _dbContext.ValidationIssues.Where(i => i.LabResultId == result.Id && !i.IsResolved).ToListAsync(cancellationToken)) { issue.IsResolved = true; issue.ResolutionType = "ResolvedByCorrection"; issue.ResolvedAt = now; }
        result.ReviewRequired = false;
        var value = result.CorrectedValueNumeric ?? result.ValueNumeric;
        var min = result.CorrectedReferenceMin ?? result.ReferenceMin;
        var max = result.CorrectedReferenceMax ?? result.ReferenceMax;
        result.CalculatedStatus = _rangeClassifier.Classify(value, min, max, result.ReferenceType);
        _dbContext.ResultCorrectionAudits.AddRange(audit);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await Audit("RESULT_CORRECT", "LabResult", resultId, true, cancellationToken);
        return Ok(MapResultToDto(result, report));
    }

    [HttpGet("{reportId:guid}/results/{resultId:guid}/audit")]
    public async Task<IActionResult> GetCorrectionAudit(Guid reportId, Guid resultId, CancellationToken cancellationToken)
    {
        if (_enforceAuthorization && !await _authorization.CanViewReportAsync(User, reportId, cancellationToken)) { await Audit("ACCESS_DENIED", "LabResult", resultId, false, cancellationToken); return NotFound(new { error = "Result was not found." }); }
        var exists = await _dbContext.LabResults.AnyAsync(r => r.Id == resultId && r.MedicalReportId == reportId, cancellationToken);
        if (!exists) return NotFound(new { error = "Result was not found." });
        var entries = await _dbContext.ResultCorrectionAudits.AsNoTracking().Where(a => a.LabResultId == resultId).ToListAsync(cancellationToken);
        // SQLite cannot order DateTimeOffset values; this already bounded,
        // per-result audit list must remain readable in local development too.
        return Ok(entries.OrderBy(a => a.ChangedAt));
    }

    private Task Audit(string action, string resourceType, Guid? resourceId, bool success, CancellationToken ct) => _audit is null ? Task.CompletedTask : _audit.RecordAsync(User, action, success, resourceType, resourceId, ct);

    private static object MapToDto(MedicalReport report, List<LabResult> results)
    {
        return new
        {
            id = report.Id,
            originalFileName = report.OriginalFileName,
            storedFileName = report.StoredFileName,
            fileSize = report.FileSize,
            status = report.Status.ToString(),
            requiresOcr = report.OcrRequired,
            ocrRequired = report.OcrRequired,
            ocrApplied = report.OcrApplied,
            processingMode = report.ProcessingMode,
            pageSources = System.Text.Json.JsonSerializer.Deserialize<List<AiPageSourceDto>>(report.PageSourcesJson ?? "null"),
            uploadedAt = report.UploadedAt,
            analysedAt = report.AnalysedAt,
            reportDate = report.ReportDate ?? report.UploadedAt,
            reportDateSource = report.ReportDateSource,
            documentType = report.DocumentType,
            documentTypeConfidence = report.DocumentTypeConfidence,
            documentTypeSignals = System.Text.Json.JsonSerializer.Deserialize<List<string>>(report.DocumentTypeSignalsJson ?? "[]"),
            structuredData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(report.StructuredDataJson ?? "{}"),
            resultsCount = results.Count,
            validationSummary = new { totalResults = results.Count, autoAccepted = results.Count(r => !r.ReviewRequired), reviewRequired = results.Count(r => r.ReviewRequired), verified = results.Count(r => r.IsVerified), corrected = results.Count(r => r.CorrectedAt.HasValue) },
            results = results.Select(r => MapResultToDto(r, report)).ToList()
        };
    }

    private static object MapResultToDto(LabResult result, MedicalReport? report = null)
    {
        var pageSource = report?.PageSourcesJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<List<AiPageSourceDto>>(report.PageSourcesJson)
            ?.FirstOrDefault(p => p.Page == result.PageNumber)?.Source;
        return new
        {
            id = result.Id,
            medicalReportId = result.MedicalReportId,
            originalTestName = result.OriginalTestName,
            normalizedTestName = result.CorrectedTestName ?? result.NormalizedTestName,
            valueNumeric = result.CorrectedValueNumeric ?? result.ValueNumeric,
            valueText = result.CorrectedValueText ?? result.ValueText,
            unit = result.CorrectedUnit ?? result.Unit,
            originalUnit = result.OriginalUnit,
            normalizedUnit = result.NormalizedUnit,
            valueOperator = result.ValueOperator,
            referenceMin = result.CorrectedReferenceMin ?? result.ReferenceMin,
            referenceMax = result.CorrectedReferenceMax ?? result.ReferenceMax,
            referenceText = result.CorrectedReferenceText ?? result.ReferenceText,
            referenceType = result.ReferenceType,
            referenceOperator = result.ReferenceOperator,
            reportedFlag = result.ReportedFlag,
            sectionName = result.SectionName,
            methodText = result.MethodText,
            reviewRequired = result.ReviewRequired,
            ambiguityReason = result.AmbiguityReason,
            flagDiscrepancy = result.FlagDiscrepancy,
            reviewState = result.IsVerified ? "HUMAN_VERIFIED" : result.CorrectedAt.HasValue ? "HUMAN_CORRECTED" : result.ReviewRequired ? "REVIEW_REQUIRED" : "AUTO_ACCEPTED",
            validationIssues = result.ValidationIssues.OrderByDescending(i => i.Severity).Select(i => new { id = i.Id, code = i.Code, severity = i.Severity, field = i.FieldName, message = i.Message, requiresReview = i.RequiresReview, isResolved = i.IsResolved, resolutionType = i.ResolutionType, createdAt = i.CreatedAt, resolvedAt = i.ResolvedAt }).ToList(),
            extractedOriginalTestName = result.OriginalTestName,
            extractedValueNumeric = result.ValueNumeric,
            extractedValueText = result.ValueText,
            extractedUnit = result.Unit,
            extractedReferenceMin = result.ReferenceMin,
            extractedReferenceMax = result.ReferenceMax,
            extractedReferenceText = result.ReferenceText,
            calculatedStatus = result.CalculatedStatus.ToString(),
            extractionConfidence = result.ExtractionConfidence,
            pageNumber = result.PageNumber,
            sourceType = pageSource,
            boundingBoxJson = result.BoundingBoxJson,
            isCorrected = result.CorrectedAt.HasValue,
            correctionReason = result.CorrectionReason,
            correctedAt = result.CorrectedAt,
            lowConfidence = result.ExtractionConfidence < 0.8m,
            isVerified = result.IsVerified,
            createdAt = result.CreatedAt
        };
    }
}
