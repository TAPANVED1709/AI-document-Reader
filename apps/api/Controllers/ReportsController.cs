using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
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
        var reportExists = await _dbContext.MedicalReports.AnyAsync(r => r.Id == id, cancellationToken);
        if (!reportExists)
        {
            return NotFound(new { error = $"Medical report '{id}' was not found." });
        }

        var results = await _dbContext.LabResults
            .Where(l => l.MedicalReportId == id)
            .OrderBy(l => l.PageNumber)
            .ThenBy(l => l.OriginalTestName)
            .ToListAsync(cancellationToken);

        return Ok(results.Select(MapResultToDto).ToList());
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
            results = results.Select(MapResultToDto).ToList()
        };
    }

    private static object MapResultToDto(LabResult result)
    {
        return new
        {
            id = result.Id,
            medicalReportId = result.MedicalReportId,
            originalTestName = result.OriginalTestName,
            normalizedTestName = result.NormalizedTestName,
            valueNumeric = result.ValueNumeric,
            valueText = result.ValueText,
            unit = result.Unit,
            referenceMin = result.ReferenceMin,
            referenceMax = result.ReferenceMax,
            referenceText = result.ReferenceText,
            calculatedStatus = result.CalculatedStatus.ToString(),
            extractionConfidence = result.ExtractionConfidence,
            pageNumber = result.PageNumber,
            boundingBoxJson = result.BoundingBoxJson,
            lowConfidence = result.ExtractionConfidence < 0.8m,
            isVerified = result.IsVerified,
            createdAt = result.CreatedAt
        };
    }
}
