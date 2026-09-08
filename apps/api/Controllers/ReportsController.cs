using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AI.DocumentReader.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    public record ResultCorrectionRequest(
        [property: MaxLength(255)] string? TestName,
        decimal? Value,
        [property: MaxLength(100)] string? ValueText,
        [property: MaxLength(50)] string? Unit,
        decimal? ReferenceMin,
        decimal? ReferenceMax,
        [property: MaxLength(100)] string? ReferenceText,
        [property: MaxLength(1000)] string? Reason,
        [property: MaxLength(255)] string? CorrectedBy);

    private readonly DocumentDbContext _dbContext;
    private readonly ILocalStorageService _storageService;
    private readonly IAiServiceClient _aiServiceClient;
    private readonly IReferenceRangeClassifier _rangeClassifier;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        DocumentDbContext dbContext,
        ILocalStorageService storageService,
        IAiServiceClient aiServiceClient,
        IReferenceRangeClassifier rangeClassifier,
        ILogger<ReportsController> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _aiServiceClient = aiServiceClient;
        _rangeClassifier = rangeClassifier;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a PDF medical laboratory report and executes document intelligence analysis.
    /// </summary>
    [HttpPost("upload")]
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
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 3. Invoke Python AI Intelligence Service
        AiAnalysisResponseDto aiResponse;
        try
        {
            aiResponse = await _aiServiceClient.AnalyseDocumentAsync(absolutePath, file.FileName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI analysis execution failed for report {ReportId}.", report.Id);
            report.Status = ReportStatus.Failed;
            analysisRun.Status = AnalysisStatus.Failed;
            analysisRun.CompletedAt = DateTimeOffset.UtcNow;
            analysisRun.ErrorMessage = ex.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "AI Document Intelligence service encountered an error while processing the report.",
                details = ex.Message,
                reportId = report.Id
            });
        }

        report.OcrRequired = aiResponse.RequiresOcr;
        report.OcrApplied = aiResponse.OcrApplied;
        report.ProcessingMode = aiResponse.ProcessingMode;
        report.PageSourcesJson = System.Text.Json.JsonSerializer.Serialize(aiResponse.PageSources);
        var pendingOcr = aiResponse.RequiresOcr && (!aiResponse.OcrApplied ||
            aiResponse.PageSources?.Any(p => p.Source is "OCR_UNAVAILABLE" or "OCR_FAILED") == true);

        if (aiResponse.OcrApplied)
        {
            _logger.LogInformation(
                "Document {ReportId} was processed via local OCR. Extracted {Count} result(s).",
                report.Id, aiResponse.Results.Count);
        }

        // 5. Apply deterministic reference-range classification and save results
        var labResults = new List<LabResult>();
        foreach (var extracted in aiResponse.Results)
        {
            // CRITICAL SAFETY REQUIREMENT:
            // Classification strictly uses reference bounds extracted from the document
            var calculatedStatus = _rangeClassifier.Classify(
                extracted.Value,
                extracted.ReferenceMin,
                extracted.ReferenceMax
            );

            var resultEntity = new LabResult
            {
                MedicalReportId = report.Id,
                OriginalTestName = extracted.OriginalName,
                NormalizedTestName = extracted.NormalizedName,
                ValueNumeric = extracted.Value,
                ValueText = extracted.ValueText,
                Unit = extracted.Unit,
                ReferenceMin = extracted.ReferenceMin,
                ReferenceMax = extracted.ReferenceMax,
                ReferenceText = extracted.ReferenceText,
                CalculatedStatus = calculatedStatus,
                ExtractionConfidence = extracted.Confidence,
                PageNumber = extracted.Page,
                BoundingBoxJson = extracted.BoundingBoxJson,
                CreatedAt = DateTimeOffset.UtcNow
            };

            labResults.Add(resultEntity);
            _dbContext.LabResults.Add(resultEntity);
        }

        report.Status = pendingOcr ? ReportStatus.RequiresOcr : ReportStatus.Completed;
        report.AnalysedAt = DateTimeOffset.UtcNow;

        analysisRun.Status = AnalysisStatus.Completed;
        analysisRun.CompletedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Report {ReportId} successfully analysed. {Count} tests saved.", report.Id, labResults.Count);

        return CreatedAtAction(nameof(GetReportById), new { id = report.Id }, MapToDto(report, labResults));
    }

    /// <summary>
    /// Retrieves a medical report's metadata and processing status.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetReportById(Guid id, CancellationToken cancellationToken)
    {
        var report = await _dbContext.MedicalReports
            .Include(r => r.LabResults)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (report == null)
        {
            return NotFound(new { error = $"Medical report '{id}' was not found." });
        }

        return Ok(MapToDto(report, report.LabResults.ToList()));
    }

    /// <summary>
    /// Retrieves only the extracted lab results for a report.
    /// </summary>
    [HttpGet("{id:guid}/results")]
    public async Task<IActionResult> GetReportResults(Guid id, CancellationToken cancellationToken)
    {
        var report = await _dbContext.MedicalReports.Include(r => r.LabResults).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
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
        var report = _dbContext.MedicalReports.AsNoTracking().FirstOrDefault(r => r.Id == id);
        if (report == null || !_storageService.FileExists(report.StoredFileName)) return NotFound(new { error = "Report file was not found." });
        return PhysicalFile(_storageService.GetReportAbsolutePath(report.StoredFileName), "application/pdf", report.OriginalFileName, enableRangeProcessing: true);
    }

    [HttpPost("{reportId:guid}/results/{resultId:guid}/verify")]
    public async Task<IActionResult> VerifyResult(Guid reportId, Guid resultId, CancellationToken cancellationToken)
    {
        var result = await _dbContext.LabResults.FirstOrDefaultAsync(r => r.Id == resultId && r.MedicalReportId == reportId, cancellationToken);
        if (result == null) return NotFound(new { error = "Result was not found." });
        result.IsVerified = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { isVerified = true });
    }

    [HttpPatch("{reportId:guid}/results/{resultId:guid}")]
    public async Task<IActionResult> CorrectResult(Guid reportId, Guid resultId, [FromBody] ResultCorrectionRequest request, CancellationToken cancellationToken)
    {
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
        var value = result.CorrectedValueNumeric ?? result.ValueNumeric;
        var min = result.CorrectedReferenceMin ?? result.ReferenceMin;
        var max = result.CorrectedReferenceMax ?? result.ReferenceMax;
        result.CalculatedStatus = _rangeClassifier.Classify(value, min, max);
        _dbContext.ResultCorrectionAudits.AddRange(audit);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapResultToDto(result, report));
    }

    [HttpGet("{reportId:guid}/results/{resultId:guid}/audit")]
    public async Task<IActionResult> GetCorrectionAudit(Guid reportId, Guid resultId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.LabResults.AnyAsync(r => r.Id == resultId && r.MedicalReportId == reportId, cancellationToken);
        if (!exists) return NotFound(new { error = "Result was not found." });
        var entries = await _dbContext.ResultCorrectionAudits.AsNoTracking().Where(a => a.LabResultId == resultId).OrderBy(a => a.ChangedAt).ToListAsync(cancellationToken);
        return Ok(entries);
    }

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
            resultsCount = results.Count,
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
            referenceMin = result.CorrectedReferenceMin ?? result.ReferenceMin,
            referenceMax = result.CorrectedReferenceMax ?? result.ReferenceMax,
            referenceText = result.CorrectedReferenceText ?? result.ReferenceText,
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
